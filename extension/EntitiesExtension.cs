using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RobloxCSharp.Common.Diagnostics;
using RobloxCSharp.Plugins;
using RobloxCSharp.Rojo;
using RobloxCSharp.Transformer;
using RobloxCSharp.Transformer.AST;
using RobloxCSharp.Transformer.Extensibility;

namespace RobloxCSharp.Extensions.Entities
{
	// PreSourceDiscovery is the heart of this plugin. It runs once per
	// `roblox-csharp dev` tick before the main pipeline enumerates src/.
	// We parse the user's .cs files (plus our stubs) with a lightweight
	// Roslyn compilation, find every IComponent class tagged with a
	// [Context]-derived attribute, and write Generated/{Ctx}*.cs files
	// into src/Generated/. The main pipeline then sees those files like
	// any other source.
	//
	// Scope (alpha):
	//   - Flag components (no public fields) → bool property on Entity.
	//   - Value components (public fields) → AddX/ReplaceX/RemoveX/hasX.
	//   - Multiple contexts ([Game], [Input], ...).
	//   - Context discovery is by attribute base-type lookup: any
	//     attribute whose class inherits from ContextAttribute counts.
	// Out of scope this slice:
	//   - [Unique], [EntityIndex], [PrimaryEntityIndex] codegen — flagged
	//     by `// TODO`s in the emitted source for the follow-up.
	public sealed class EntitiesExtension : IRobloxCSharpExtension
	{
		private const string PluginName = "Entities";
		private const string GeneratedFolder = "Generated";

		public string Name => "Entities.Codegen";

		public void PreSourceDiscovery(string srcDir, IReadOnlyList<Plugin> plugins, DiagnosticBag diagnostics)
		{
			Plugin self = plugins.FirstOrDefault(p => string.Equals(p.Name, PluginName, StringComparison.Ordinal));
			if (self is null) return;

			string stubsDir = Path.Combine(self.RootDir, "stubs");
			List<SyntaxTree> trees = new();
			AppendCsFiles(srcDir, trees);
			AppendCsFiles(stubsDir, trees);

			Compilation compilation = CSharpCompilation.Create(
				assemblyName: "Entities.Codegen.Scan",
				syntaxTrees: trees,
				references: BuildReferences(),
				options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

			AttributeSymbols attrs = new()
			{
				Component = compilation.GetTypeByMetadataName("Entities.IComponent"),
				Context = compilation.GetTypeByMetadataName("Entities.CodeGeneration.Attributes.ContextAttribute"),
				Replicated = compilation.GetTypeByMetadataName("Entities.CodeGeneration.Attributes.ReplicatedAttribute"),
				Unique = compilation.GetTypeByMetadataName("Entities.CodeGeneration.Attributes.UniqueAttribute"),
				EntityIndex = compilation.GetTypeByMetadataName("Entities.CodeGeneration.Attributes.EntityIndexAttribute"),
				PrimaryEntityIndex = compilation.GetTypeByMetadataName("Entities.CodeGeneration.Attributes.PrimaryEntityIndexAttribute"),
				PostConstructor = compilation.GetTypeByMetadataName("Entities.CodeGeneration.Attributes.PostConstructorAttribute"),
				Watched = compilation.GetTypeByMetadataName("Entities.CodeGeneration.Attributes.WatchedAttribute"),
			};
			if (attrs.Component is null || attrs.Context is null) return;

			Dictionary<string, ContextModel> byContext = new(StringComparer.Ordinal);

			// [PostConstructor] scan — walk every `partial class Contexts` in
			// user code, collect names of methods carrying the attribute.
			// ContextsTemplate appends these as trailing calls in the
			// generated Contexts() ctor so user-side index registration etc.
			// runs after every per-context field is wired.
			List<string> postConstructors = new();

			foreach (SyntaxTree tree in trees)
			{
				// Skip our own stubs; we only want user code.
				if (tree.FilePath.StartsWith(stubsDir, StringComparison.OrdinalIgnoreCase)) continue;

				SemanticModel model = compilation.GetSemanticModel(tree);
				foreach (ClassDeclarationSyntax classDecl in tree.GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>())
				{
					INamedTypeSymbol typeSymbol = model.GetDeclaredSymbol(classDecl) as INamedTypeSymbol;
					if (typeSymbol is null) continue;

					// PostConstructor scan on `partial class Contexts`.
					if (typeSymbol.Name == "Contexts" && attrs.PostConstructor is not null)
					{
						foreach (MethodDeclarationSyntax methodDecl in classDecl.Members.OfType<MethodDeclarationSyntax>())
						{
							IMethodSymbol methodSymbol = model.GetDeclaredSymbol(methodDecl) as IMethodSymbol;
							if (methodSymbol is null) continue;
							if (AttributeSymbols.HasAttribute(methodSymbol, attrs.PostConstructor))
							{
								if (!postConstructors.Contains(methodSymbol.Name)) postConstructors.Add(methodSymbol.Name);
							}
						}
					}

					if (typeSymbol.IsAbstract) continue;
					if (!ImplementsInterface(typeSymbol, attrs.Component)) continue;

					foreach (AttributeData attr in typeSymbol.GetAttributes())
					{
						if (!InheritsFrom(attr.AttributeClass, attrs.Context)) continue;
						string contextName = ExtractContextName(attr);
						if (string.IsNullOrEmpty(contextName)) continue;

						if (!byContext.TryGetValue(contextName, out ContextModel ctx))
						{
							ctx = new ContextModel(contextName);
							byContext[contextName] = ctx;
						}
						ctx.Components.Add(new ComponentModel(typeSymbol, attrs));
					}
				}
			}

			if (byContext.Count == 0) return;

			string genDir = Path.Combine(srcDir, GeneratedFolder);
			Directory.CreateDirectory(genDir);
			string componentsDir = Path.Combine(genDir, "Components");
			Directory.CreateDirectory(componentsDir);
			string watchedDir = Path.Combine(genDir, "Watched");

			HashSet<string> expectedComponentFiles = new(StringComparer.OrdinalIgnoreCase);
			HashSet<string> expectedWatchedFiles = new(StringComparer.OrdinalIgnoreCase);

			// [Watched] synthesis — for every [Watched] component, append a
			// {Name}Changed flag ComponentModel to the same context and
			// emit a C# class file for it so user code can reference the
			// type. The flag flows through the normal codegen pipeline
			// from here (gets a lookup index, a matcher, an IsXChanged
			// entity property).
			foreach (ContextModel ctx in byContext.Values)
			{
				List<ComponentModel> toSynthesize = ctx.Components.Where(c => c.IsWatched).ToList();
				if (toSynthesize.Count > 0) Directory.CreateDirectory(watchedDir);
				foreach (ComponentModel src in toSynthesize)
				{
					ComponentModel changed = ComponentModel.CreateChangedFlag(src);
					ctx.Components.Add(changed);

					string fileName = $"{src.TypeName}Changed.cs";
					WriteFile(watchedDir, fileName, WatchedFlagClassTemplate.Emit(src));
					expectedWatchedFiles.Add(fileName);
				}
			}

			foreach (ContextModel ctx in byContext.Values)
			{
				ctx.Components.Sort((a, b) => string.Compare(a.TypeName, b.TypeName, StringComparison.Ordinal));
				WriteFile(genDir, $"{ctx.Name}ComponentsLookup.cs", ComponentsLookupTemplate.Emit(ctx));
				WriteFile(genDir, $"{ctx.Name}Context.cs", ContextTemplate.Emit(ctx));
				WriteFile(genDir, $"{ctx.Name}Entity.cs", EntityTemplate.Emit(ctx));
				WriteFile(genDir, $"{ctx.Name}Matcher.cs", MatcherTemplate.Emit(ctx));

				// Per-component partial — one file per component holds the
				// entity property/methods + the matcher static getter for
				// that single component. The transpiler merges every
				// `partial class GameEntity` across these files back into
				// one Luau metatable-class.
				foreach (ComponentModel c in ctx.Components)
				{
					string fileName = $"{ctx.Name}.{c.TypeName}.cs";
					WriteFile(componentsDir, fileName, ComponentTemplate.Emit(ctx, c));
					expectedComponentFiles.Add(fileName);
				}

				// Replication — codegen-emitted entity AddX/ReplaceX/RemoveX
				// bodies enqueue into the per-context buffer in
				// EntitiesReplication (the call is in EntityTemplate). Two
				// companion files per context:
				//   {Ctx}ServerReplication — registers a snapshotter for
				//     late-join (walks every live entity + replicated
				//     component, builds a Set-op batch).
				//   {Ctx}ClientReplication — subscribes once and dispatches
				//     received ops by componentIndex onto the local entity.
				List<ComponentModel> replicated = ctx.Components.Where(c => c.IsReplicated).ToList();
				string serverReplicationFile = Path.Combine(genDir, $"{ctx.Name}ServerReplication.cs");
				string clientReplicationFile = Path.Combine(genDir, $"{ctx.Name}ClientReplication.cs");
				if (replicated.Count > 0)
				{
					WriteFile(genDir, $"{ctx.Name}ServerReplication.cs", ServerReplicationTemplate.Emit(ctx, replicated));
					WriteFile(genDir, $"{ctx.Name}ClientReplication.cs", ClientReplicationTemplate.Emit(ctx, replicated));
				}
				else
				{
					if (File.Exists(serverReplicationFile)) File.Delete(serverReplicationFile);
					if (File.Exists(clientReplicationFile)) File.Delete(clientReplicationFile);
				}

				// [Watched] cleanup — emit per context when any component
				// in the context is [Watched]. The user adds it to the
				// tail of their feature pipeline; it clears every Changed
				// flag at end of frame so reactive systems see one-frame
				// signals.
				List<ComponentModel> watchedSources = ctx.Components
					.Where(c => c.IsWatched && !c.IsSynthesizedChangedFlag)
					.ToList();
				string cleanupFile = Path.Combine(genDir, $"{ctx.Name}WatchedCleanupSystem.cs");
				if (watchedSources.Count > 0)
				{
					WriteFile(genDir, $"{ctx.Name}WatchedCleanupSystem.cs",
						WatchedCleanupSystemTemplate.Emit(ctx, watchedSources));
				}
				else if (File.Exists(cleanupFile))
				{
					File.Delete(cleanupFile);
				}

				// Sweep files from prior slice shapes — the per-component
				// fanout (Replication.cs static delegate class) and the
				// renamed-from ClientMirror variants. Always delete; the
				// codegen never emits them again.
				string staleReplicationFile = Path.Combine(genDir, $"{ctx.Name}Replication.cs");
				if (File.Exists(staleReplicationFile)) File.Delete(staleReplicationFile);
				string staleMirrorFile = Path.Combine(genDir, $"{ctx.Name}ClientMirror.cs");
				if (File.Exists(staleMirrorFile)) File.Delete(staleMirrorFile);
				string staleMirrorClient = Path.Combine(genDir, $"{ctx.Name}ClientMirror.client.cs");
				if (File.Exists(staleMirrorClient)) File.Delete(staleMirrorClient);
			}

			WriteFile(genDir, "Contexts.cs", ContextsTemplate.Emit(byContext.Values.OrderBy(c => c.Name, StringComparer.Ordinal).ToList(), postConstructors));

			// Reap stale per-component files (component renamed, deleted,
			// attribute removed) so the codegen stays idempotent.
			foreach (string existing in Directory.EnumerateFiles(componentsDir, "*.cs"))
			{
				string name = Path.GetFileName(existing);
				if (!expectedComponentFiles.Contains(name)) File.Delete(existing);
			}

			// Same reaping for the [Watched] Changed-flag class files. If
			// a user drops [Watched] from a component, the synthesized
			// {Name}Changed.cs from a prior run disappears.
			if (Directory.Exists(watchedDir))
			{
				foreach (string existing in Directory.EnumerateFiles(watchedDir, "*.cs"))
				{
					string name = Path.GetFileName(existing);
					if (!expectedWatchedFiles.Contains(name)) File.Delete(existing);
				}
			}
		}

		public void OnCompile(Compilation compilation, IReadOnlyList<Plugin> plugins, DiagnosticBag diagnostics) { }

		public LuaNode TryRewrite(SyntaxNode syntax, TransformerState state) => null;

		public IEnumerable<INamedTypeSymbol> ContributeImports(CompilationUnitSyntax syntax, TransformerState state)
			=> Array.Empty<INamedTypeSymbol>();

		public IEnumerable<INamedTypeSymbol> SuppressImports(CompilationUnitSyntax syntax, TransformerState state)
			=> Array.Empty<INamedTypeSymbol>();

		public void OnUnitTransformed(LuaCompilationUnit unit, CompilationUnitSyntax syntax, TransformerState state) { }

		public void EmitArtifacts(string outDir, IReadOnlyList<Plugin> plugins, RojoResolver resolver, DiagnosticBag diagnostics) { }

		// ----------------------------------------------------------------
		// Discovery helpers
		// ----------------------------------------------------------------

		private static void AppendCsFiles(string dir, List<SyntaxTree> trees)
		{
			if (!Directory.Exists(dir)) return;
			foreach (string file in Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories))
			{
				string text = File.ReadAllText(file);
				trees.Add(CSharpSyntaxTree.ParseText(text, path: file));
			}
		}

		private static List<MetadataReference> BuildReferences()
		{
			List<MetadataReference> refs = new();
			HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
			foreach (System.Reflection.Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
			{
				if (asm.IsDynamic) continue;
				string loc = asm.Location;
				if (string.IsNullOrEmpty(loc)) continue;
				if (!seen.Add(loc)) continue;
				refs.Add(MetadataReference.CreateFromFile(loc));
			}
			return refs;
		}

		private static bool ImplementsInterface(INamedTypeSymbol type, INamedTypeSymbol iface)
		{
			foreach (INamedTypeSymbol candidate in type.AllInterfaces)
			{
				if (SymbolEqualityComparer.Default.Equals(candidate, iface)) return true;
			}
			return false;
		}

		private static bool InheritsFrom(INamedTypeSymbol type, INamedTypeSymbol baseType)
		{
			INamedTypeSymbol cursor = type;
			while (cursor is not null)
			{
				if (SymbolEqualityComparer.Default.Equals(cursor, baseType)) return true;
				cursor = cursor.BaseType;
			}
			return false;
		}

		private static string ExtractContextName(AttributeData attr)
		{
			// User-declared subclass (GameAttribute -> ContextAttribute):
			// the subclass calls `base("Game")` in its constructor, so the
			// constructor arg flows up as the first AttributeData ctor
			// argument when the subclass declares no explicit ones — but
			// most subclasses do, so we fall back to the attribute class
			// name minus the "Attribute" suffix.
			if (attr.ConstructorArguments.Length > 0)
			{
				TypedConstant first = attr.ConstructorArguments[0];
				if (first.Value is string s && !string.IsNullOrEmpty(s)) return s;
			}
			string typeName = attr.AttributeClass?.Name;
			if (string.IsNullOrEmpty(typeName)) return null;
			if (typeName.EndsWith("Attribute", StringComparison.Ordinal))
				typeName = typeName.Substring(0, typeName.Length - "Attribute".Length);
			return typeName;
		}

		private static void WriteFile(string dir, string name, string content)
		{
			string path = Path.Combine(dir, name);
			if (File.Exists(path))
			{
				string existing = File.ReadAllText(path);
				if (existing == content) return;
			}
			File.WriteAllText(path, content);
		}
	}

	// ----------------------------------------------------------------
	// Model
	// ----------------------------------------------------------------

	internal sealed class ContextModel
	{
		public string Name { get; }
		public List<ComponentModel> Components { get; } = new();

		public ContextModel(string name) { Name = name; }
	}

	// Bundle of attribute symbols looked up once from the Roslyn
	// compilation. Passed to ComponentModel so it doesn't have to re-query
	// per field; also lets PreSourceDiscovery share the same lookups for
	// scanning [PostConstructor] methods on partial Contexts.
	internal sealed class AttributeSymbols
	{
		public INamedTypeSymbol Component;
		public INamedTypeSymbol Context;
		public INamedTypeSymbol Replicated;
		public INamedTypeSymbol Unique;
		public INamedTypeSymbol EntityIndex;
		public INamedTypeSymbol PrimaryEntityIndex;
		public INamedTypeSymbol PostConstructor;
		public INamedTypeSymbol Watched;

		public static bool HasAttribute(ISymbol symbol, INamedTypeSymbol attr)
		{
			if (attr is null || symbol is null) return false;
			foreach (AttributeData a in symbol.GetAttributes())
			{
				if (SymbolEqualityComparer.Default.Equals(a.AttributeClass, attr)) return true;
			}
			return false;
		}
	}

	internal sealed class ComponentModel
	{
		public INamedTypeSymbol Symbol { get; }
		public string TypeName { get; }
		public string FullName { get; }
		public string NamespaceName { get; }
		public List<ComponentField> Fields { get; }
		public bool IsFlag => Fields.Count == 0;
		public bool IsReplicated { get; }
		public bool IsUnique { get; }
		public bool IsWatched { get; }
		// True for the synthesized {X}Changed flag we auto-generate for
		// every [Watched] component. Codegen needs to know so it can skip
		// emitting hook code on the Changed itself (no Changed-of-Changed).
		public bool IsSynthesizedChangedFlag { get; }
		public bool HasIndexedField
		{
			get
			{
				for (int i = 0; i < Fields.Count; i++) if (Fields[i].IsIndexed) return true;
				return false;
			}
		}

		public ComponentModel(INamedTypeSymbol symbol, AttributeSymbols attrs)
		{
			Symbol = symbol;
			TypeName = symbol.Name;
			IsReplicated = AttributeSymbols.HasAttribute(symbol, attrs.Replicated);
			IsUnique = AttributeSymbols.HasAttribute(symbol, attrs.Unique);
			IsWatched = AttributeSymbols.HasAttribute(symbol, attrs.Watched);
			IsSynthesizedChangedFlag = false;
			FullName = symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
			NamespaceName = symbol.ContainingNamespace?.IsGlobalNamespace == false
				? symbol.ContainingNamespace.ToDisplayString()
				: null;
			Fields = new List<ComponentField>();
			foreach (ISymbol member in symbol.GetMembers())
			{
				if (member is IFieldSymbol field && field.DeclaredAccessibility == Accessibility.Public && !field.IsStatic && !field.IsConst)
				{
					FieldIndexKind kind = FieldIndexKind.None;
					if (AttributeSymbols.HasAttribute(field, attrs.PrimaryEntityIndex)) kind = FieldIndexKind.PrimaryIndex;
					else if (AttributeSymbols.HasAttribute(field, attrs.EntityIndex)) kind = FieldIndexKind.Index;
					Fields.Add(new ComponentField(field.Name, field.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), kind));
				}
				// Property-shaped values (`public int Value { get; set; }`) are
				// out of scope — frozen-feast uses plain fields everywhere — so
				// we keep this strict for the alpha and flag it for follow-up
				// if a user reports needing them.
			}
		}

		// Synthesized {Name}Changed flag for a [Watched] component. No
		// Roslyn symbol exists — codegen also emits the C# class file
		// (in PreSourceDiscovery) so user code can reference the type.
		// The synthesized model carries no attributes (no Replicated,
		// Unique, etc.) and is a pure flag; its only job is to be a
		// marker that the cleanup system can find.
		private ComponentModel(ComponentModel source)
		{
			Symbol = null;
			TypeName = source.TypeName + "Changed";
			NamespaceName = source.NamespaceName;
			FullName = source.NamespaceName is null
				? "global::" + TypeName
				: "global::" + source.NamespaceName + "." + TypeName;
			Fields = new List<ComponentField>();
			IsReplicated = false;
			IsUnique = false;
			IsWatched = false;
			IsSynthesizedChangedFlag = true;
		}

		public static ComponentModel CreateChangedFlag(ComponentModel source) => new(source);
	}

	internal enum FieldIndexKind { None, Index, PrimaryIndex }

	internal readonly struct ComponentField
	{
		public string Name { get; }
		public string TypeFullName { get; }
		public FieldIndexKind IndexKind { get; }
		public ComponentField(string name, string typeFullName, FieldIndexKind indexKind = FieldIndexKind.None)
		{
			Name = name; TypeFullName = typeFullName; IndexKind = indexKind;
		}
		public bool IsIndexed => IndexKind != FieldIndexKind.None;
		public bool IsPrimaryIndexed => IndexKind == FieldIndexKind.PrimaryIndex;
	}

	// ----------------------------------------------------------------
	// Code-gen templates
	// ----------------------------------------------------------------

	internal static class ComponentsLookupTemplate
	{
		public static string Emit(ContextModel ctx)
		{
			StringBuilder sb = new();
			sb.AppendLine("// <auto-generated/> roblox-csharp-entities");
			sb.AppendLine("using Entities;");
			// `typeof(global::Code.Infrastructure.X)` carries the namespace
			// path through to the Lua emit verbatim — but the transpiler
			// only binds the bare type name as a local from CS.import. So
			// for typeof() we need `typeof(X)` (bare name), which needs
			// `using <namespace>;` at the top of the generated file. Pull
			// every component's containing namespace into the using set.
			HashSet<string> namespaces = new(StringComparer.Ordinal);
			foreach (ComponentModel c in ctx.Components)
			{
				string ns = c.NamespaceName;
				if (!string.IsNullOrEmpty(ns)) namespaces.Add(ns);
			}
			foreach (string ns in namespaces.OrderBy(s => s, StringComparer.Ordinal))
			{
				sb.AppendLine($"using {ns};");
			}
			sb.AppendLine();
			sb.AppendLine($"public static class {ctx.Name}ComponentsLookup");
			sb.AppendLine("{");
			for (int i = 0; i < ctx.Components.Count; i++)
			{
				sb.AppendLine($"\tpublic const int {ctx.Components[i].TypeName} = {i};");
			}
			sb.AppendLine($"\tpublic const int TotalComponents = {ctx.Components.Count};");
			sb.AppendLine();
			sb.AppendLine("\tpublic static readonly string[] componentNames =");
			sb.AppendLine("\t{");
			foreach (ComponentModel c in ctx.Components) sb.AppendLine($"\t\t\"{c.TypeName}\",");
			sb.AppendLine("\t};");
			sb.AppendLine();
			sb.AppendLine("\tpublic static readonly System.Type[] componentTypes =");
			sb.AppendLine("\t{");
			foreach (ComponentModel c in ctx.Components) sb.AppendLine($"\t\ttypeof({c.TypeName}),");
			sb.AppendLine("\t};");
			sb.AppendLine("}");
			return sb.ToString();
		}
	}

	internal static class ContextTemplate
	{
		public static string Emit(ContextModel ctx)
		{
			StringBuilder sb = new();
			sb.AppendLine("// <auto-generated/> roblox-csharp-entities");
			sb.AppendLine("using Entities;");
			sb.AppendLine();
			sb.AppendLine($"public sealed partial class {ctx.Name}Context : Context<{ctx.Name}Entity>");
			sb.AppendLine("{");
			sb.AppendLine($"\tpublic {ctx.Name}Context() : base({ctx.Name}ComponentsLookup.TotalComponents) {{ }}");
			sb.AppendLine($"\tpublic override {ctx.Name}Entity CreateEntityInstance() {{ return new {ctx.Name}Entity(); }}");
			sb.AppendLine("}");
			return sb.ToString();
		}
	}

	internal static class EntityTemplate
	{
		// Base partial — the shell with the inheritance line, plus (when
		// the context has any hooked components) a Destroy override that
		// pre-fires RemoveX / IsX = false so [Replicated] QueueRemove ops,
		// [Unique] _Clear hooks, and [EntityIndex] _Unregister hooks all
		// run before the base teardown wipes _components without notifying.
		// Skips the override entirely for hook-free contexts so the
		// transpiler doesn't waste a method on a no-op.
		public static string Emit(ContextModel ctx)
		{
			List<ComponentModel> hooked = ctx.Components
				.Where(c => c.IsReplicated || c.IsUnique || c.HasIndexedField)
				.ToList();

			StringBuilder sb = new();
			sb.AppendLine("// <auto-generated/> roblox-csharp-entities");
			sb.AppendLine("using Entities;");
			sb.AppendLine();
			if (hooked.Count == 0)
			{
				sb.AppendLine($"public sealed partial class {ctx.Name}Entity : Entity {{ }}");
				return sb.ToString();
			}

			sb.AppendLine($"public sealed partial class {ctx.Name}Entity : Entity");
			sb.AppendLine("{");
			sb.AppendLine("\tpublic override void Destroy()");
			sb.AppendLine("\t{");
			foreach (ComponentModel c in hooked)
			{
				if (c.IsFlag)
					sb.AppendLine($"\t\tif (Is{c.TypeName}) Is{c.TypeName} = false;");
				else
					sb.AppendLine($"\t\tif (Has{c.TypeName}) Remove{c.TypeName}();");
			}
			sb.AppendLine("\t\tbase.Destroy();");
			sb.AppendLine("\t}");
			sb.AppendLine("}");
			return sb.ToString();
		}

		internal static void EmitFlag(StringBuilder sb, ContextModel ctx, ComponentModel c, string lookup)
		{
			// Flag → `entity.Is{Name}` (PascalCase, gettable+settable bool).
			// Setter swaps the shared singleton instance in/out so we don't
			// allocate per flip. Replicated flags enqueue Set/Remove ops on
			// the per-context replication buffer, guarded by ShouldEmit so
			// the client / suppress scopes skip the enqueue. [Unique] flags
			// tail-update the context's singleton field through the
			// codegen-emitted _Set/_Clear hook — the entity API stays the
			// same shape, the context just tracks who holds the flag.
			//
			// Context-capture pattern (`{ var _ctx = (Ctx)context; if … }`)
			// halves the `self:context()` accessor count in Lua: the cast
			// evaluates `context` once and the null check then reads the
			// local — instead of two accessor calls per hook fire.
			string fireAdd = c.IsReplicated
				? $" if (EntitiesReplication.ShouldEmit()) EntitiesReplication.QueueSet(\"{ctx.Name}\", {lookup}, creationIndex);"
				: "";
			string fireRemove = c.IsReplicated
				? $" if (EntitiesReplication.ShouldEmit()) EntitiesReplication.QueueRemove(\"{ctx.Name}\", {lookup}, creationIndex);"
				: "";
			string uniqueSet = c.IsUnique
				? $" {{ {ctx.Name}Context _ctx = ({ctx.Name}Context)context; if (_ctx != null) _ctx._Set{c.TypeName}Entity(this); }}"
				: "";
			string uniqueClear = c.IsUnique
				? $" {{ {ctx.Name}Context _ctx = ({ctx.Name}Context)context; if (_ctx != null) _ctx._Clear{c.TypeName}Entity(); }}"
				: "";
			// [Watched] flag — both branches of the Is{X} setter raise the
			// Changed signal because a flag flip is a change in either
			// direction. Reactive systems gate on `Is{X}Changed`; the
			// codegen-emitted cleanup system wipes it at end of frame.
			string watchedFlag = c.IsWatched
				? $" Is{c.TypeName}Changed = true;"
				: "";

			sb.AppendLine($"\tstatic readonly {c.FullName} _{c.TypeName}Component = new();");
			sb.AppendLine($"\tpublic bool Is{c.TypeName}");
			sb.AppendLine("\t{");
			sb.AppendLine($"\t\tget {{ return HasComponent({lookup}); }}");
			sb.AppendLine("\t\tset");
			sb.AppendLine("\t\t{");
			sb.AppendLine($"\t\t\tif (value != Is{c.TypeName})");
			sb.AppendLine("\t\t\t{");
			sb.AppendLine($"\t\t\t\tif (value) {{ AddComponent({lookup}, _{c.TypeName}Component);{fireAdd}{uniqueSet}{watchedFlag} }}");
			sb.AppendLine($"\t\t\t\telse {{ RemoveComponent({lookup});{fireRemove}{uniqueClear}{watchedFlag} }}");
			sb.AppendLine("\t\t\t}");
			sb.AppendLine("\t\t}");
			sb.AppendLine("\t}");
		}

		internal static void EmitValue(StringBuilder sb, ContextModel ctx, ComponentModel c, string lookup)
		{
			string ctorArgs = string.Join(", ", c.Fields.Select(f => $"{f.TypeFullName} new{f.Name}"));
			string fieldAssigns = string.Join(" ", c.Fields.Select(f => $"component.{f.Name} = new{f.Name};"));

			// Single field named `Value` → unwrap to the field's type:
			//   `entity.Health` returns `int`, not the Health component.
			// Anything else (multi-field, or single non-`Value` field) →
			// return the component instance so `entity.Position.X` works.
			bool unwrap = c.Fields.Count == 1 && c.Fields[0].Name == "Value";

			// Replication enqueue args — contextName + componentIndex +
			// entityId + field values (named new{Field}). Set covers both
			// Add and Replace (client distinguishes by HasX). Each enqueue
			// is guarded by ShouldEmit so client / suppress-scoped calls skip.
			string setArgs = $"\"{ctx.Name}\", {lookup}, creationIndex";
			foreach (ComponentField f in c.Fields) setArgs += ", new" + f.Name;
			string fireSet = c.IsReplicated
				? $"\t\tif (EntitiesReplication.ShouldEmit()) EntitiesReplication.QueueSet({setArgs});"
				: "";
			string fireRemove = c.IsReplicated
				? $"\t\tif (EntitiesReplication.ShouldEmit()) EntitiesReplication.QueueRemove(\"{ctx.Name}\", {lookup}, creationIndex);"
				: "";
			// [EntityIndex] / [PrimaryEntityIndex] dict maintenance — keys
			// on field values. AddX registers the new key; ReplaceX captures
			// the old key (read off `this.{Component}` before the swap) and
			// then unregisters the old + registers the new; RemoveX captures
			// the old key and unregisters. Multiple indexed fields on one
			// component are supported — each gets its own register/unregister
			// call sequence and (per IndexContextPartialTemplate) its own
			// dict + lookup method.
			int indexedFieldCount = 0;
			for (int fi = 0; fi < c.Fields.Count; fi++) if (c.Fields[fi].IsIndexed) indexedFieldCount++;
			List<ComponentField> indexedFields = c.Fields.Where(f => f.IsIndexed).ToList();
			string IndexSuffix(ComponentField f) => indexedFieldCount > 1 ? f.Name : "";
			string ReadField(ComponentField f) => (unwrap && f.Name == "Value") ? c.TypeName : $"{c.TypeName}.{f.Name}";

			// All post-mutation context hooks ([Unique] + [EntityIndex])
			// share one `{ var _ctx = (Ctx)context; if (_ctx != null) {...} }`
			// block so we only access `context` once (single self:context()
			// call in the Lua emit instead of one-per-hook). EmitContextBlock
			// stamps the boilerplate; callers append per-hook lines via the
			// stringbuilder closure.
			bool HasContextHooks() => c.IsUnique || indexedFields.Count > 0;
			void EmitContextBlock(string indent, System.Action<string> body)
			{
				if (!HasContextHooks()) return;
				sb.AppendLine($"{indent}{{");
				sb.AppendLine($"{indent}\t{ctx.Name}Context _ctx = ({ctx.Name}Context)context;");
				sb.AppendLine($"{indent}\tif (_ctx != null)");
				sb.AppendLine($"{indent}\t{{");
				body($"{indent}\t\t");
				sb.AppendLine($"{indent}\t}}");
				sb.AppendLine($"{indent}}}");
			}

			if (unwrap)
			{
				// Setter routes through CreateComponent<T> so the new
				// instance comes off the pool when one's available.
				string valueType = c.Fields[0].TypeFullName;
				string fireSetter = c.IsReplicated
					? $"\t\t\tif (EntitiesReplication.ShouldEmit()) EntitiesReplication.QueueSet(\"{ctx.Name}\", {lookup}, creationIndex, value);"
					: "";
				sb.AppendLine($"\tpublic {valueType} {c.TypeName}");
				sb.AppendLine("\t{");
				sb.AppendLine($"\t\tget {{ return (({c.FullName})GetComponent({lookup})).Value; }}");
				sb.AppendLine("\t\tset");
				sb.AppendLine("\t\t{");
				// Index prev capture — Value-indexed only (unwrap implies one field).
				if (c.Fields[0].IsIndexed)
				{
					sb.AppendLine($"\t\t\t{valueType} _prevValue = Has{c.TypeName} ? {c.TypeName} : default({valueType});");
					sb.AppendLine($"\t\t\tbool _had{c.TypeName} = Has{c.TypeName};");
				}
				sb.AppendLine($"\t\t\t{c.FullName} component = CreateComponent<{c.FullName}>({lookup});");
				sb.AppendLine($"\t\t\tcomponent.Value = value;");
				sb.AppendLine($"\t\t\tReplaceComponent({lookup}, component);");
				if (c.IsReplicated) sb.AppendLine(fireSetter);
				EmitContextBlock("\t\t\t", indent =>
				{
					if (c.IsUnique) sb.AppendLine($"{indent}_ctx._Set{c.TypeName}Entity(this);");
					if (c.Fields[0].IsIndexed)
					{
						ComponentField f = c.Fields[0];
						string suffix = IndexSuffix(f);
						sb.AppendLine($"{indent}if (_had{c.TypeName}) _ctx._Unregister{c.TypeName}{suffix}(this, _prevValue);");
						sb.AppendLine($"{indent}_ctx._Register{c.TypeName}{suffix}(this, value);");
					}
				});
				if (c.IsWatched) sb.AppendLine($"\t\t\tIs{c.TypeName}Changed = true;");
				sb.AppendLine("\t\t}");
				sb.AppendLine("\t}");
			}
			else
			{
				sb.AppendLine($"\tpublic {c.FullName} {c.TypeName} {{ get {{ return ({c.FullName})GetComponent({lookup}); }} }}");
			}
			sb.AppendLine($"\tpublic bool Has{c.TypeName} {{ get {{ return HasComponent({lookup}); }} }}");

			// AddX/ReplaceX pull from the component pool when one's
			// available. The recycled instance has whatever state was on it
			// before; the field assignments below overwrite anything we care
			// about.
			sb.AppendLine();
			sb.AppendLine($"\tpublic {c.FullName} Add{c.TypeName}({ctorArgs})");
			sb.AppendLine("\t{");
			sb.AppendLine($"\t\t{c.FullName} component = CreateComponent<{c.FullName}>({lookup});");
			sb.AppendLine($"\t\t{fieldAssigns}");
			sb.AppendLine($"\t\tAddComponent({lookup}, component);");
			if (c.IsReplicated) sb.AppendLine(fireSet);
			EmitContextBlock("\t\t", indent =>
			{
				if (c.IsUnique) sb.AppendLine($"{indent}_ctx._Set{c.TypeName}Entity(this);");
				foreach (ComponentField f in indexedFields)
				{
					string suffix = IndexSuffix(f);
					sb.AppendLine($"{indent}_ctx._Register{c.TypeName}{suffix}(this, new{f.Name});");
				}
			});
			if (c.IsWatched) sb.AppendLine($"\t\tIs{c.TypeName}Changed = true;");
			sb.AppendLine("\t\treturn component;");
			sb.AppendLine("\t}");

			sb.AppendLine();
			sb.AppendLine($"\tpublic {c.FullName} Replace{c.TypeName}({ctorArgs})");
			sb.AppendLine("\t{");
			// Capture pre-replace state for each indexed field so we can
			// unregister the old key after the swap.
			foreach (ComponentField f in indexedFields)
			{
				sb.AppendLine($"\t\t{f.TypeFullName} _prev{f.Name} = Has{c.TypeName} ? {ReadField(f)} : default({f.TypeFullName});");
			}
			if (indexedFields.Count > 0)
				sb.AppendLine($"\t\tbool _had{c.TypeName} = Has{c.TypeName};");
			sb.AppendLine($"\t\t{c.FullName} component = CreateComponent<{c.FullName}>({lookup});");
			sb.AppendLine($"\t\t{fieldAssigns}");
			sb.AppendLine($"\t\tReplaceComponent({lookup}, component);");
			if (c.IsReplicated) sb.AppendLine(fireSet);
			EmitContextBlock("\t\t", indent =>
			{
				if (c.IsUnique) sb.AppendLine($"{indent}_ctx._Set{c.TypeName}Entity(this);");
				foreach (ComponentField f in indexedFields)
				{
					string suffix = IndexSuffix(f);
					sb.AppendLine($"{indent}if (_had{c.TypeName}) _ctx._Unregister{c.TypeName}{suffix}(this, _prev{f.Name});");
					sb.AppendLine($"{indent}_ctx._Register{c.TypeName}{suffix}(this, new{f.Name});");
				}
			});
			if (c.IsWatched) sb.AppendLine($"\t\tIs{c.TypeName}Changed = true;");
			sb.AppendLine("\t\treturn component;");
			sb.AppendLine("\t}");

			sb.AppendLine();
			sb.AppendLine($"\tpublic void Remove{c.TypeName}()");
			sb.AppendLine("\t{");
			foreach (ComponentField f in indexedFields)
			{
				sb.AppendLine($"\t\t{f.TypeFullName} _prev{f.Name} = Has{c.TypeName} ? {ReadField(f)} : default({f.TypeFullName});");
			}
			if (indexedFields.Count > 0)
				sb.AppendLine($"\t\tbool _had{c.TypeName} = Has{c.TypeName};");
			sb.AppendLine($"\t\tRemoveComponent({lookup});");
			if (c.IsReplicated) sb.AppendLine(fireRemove);
			EmitContextBlock("\t\t", indent =>
			{
				if (c.IsUnique) sb.AppendLine($"{indent}_ctx._Clear{c.TypeName}Entity();");
				foreach (ComponentField f in indexedFields)
				{
					string suffix = IndexSuffix(f);
					sb.AppendLine($"{indent}if (_had{c.TypeName}) _ctx._Unregister{c.TypeName}{suffix}(this, _prev{f.Name});");
				}
			});
			sb.AppendLine("\t}");
		}
	}

	internal static class MatcherTemplate
	{
		// Base partial — only the shared AllOf/AnyOf factories live here.
		// Per-component static getters live in
		// Generated/Components/{Ctx}.{Component}.cs and merge in.
		public static string Emit(ContextModel ctx)
		{
			StringBuilder sb = new();
			sb.AppendLine("// <auto-generated/> roblox-csharp-entities");
			sb.AppendLine("using Entities;");
			sb.AppendLine();
			sb.AppendLine($"public sealed partial class {ctx.Name}Matcher");
			sb.AppendLine("{");
			sb.AppendLine($"\tpublic static IAllOfMatcher<{ctx.Name}Entity> AllOf(params IMatcher<{ctx.Name}Entity>[] matchers)");
			sb.AppendLine($"\t\t=> Matcher.AllOf<{ctx.Name}Entity>(matchers);");
			sb.AppendLine($"\tpublic static IAnyOfMatcher<{ctx.Name}Entity> AnyOf(params IMatcher<{ctx.Name}Entity>[] matchers)");
			sb.AppendLine($"\t\t=> Matcher.AnyOf<{ctx.Name}Entity>(matchers);");
			sb.AppendLine("}");
			return sb.ToString();
		}

		// Per-component static-getter body — public so ComponentTemplate
		// can splice it into the per-component .cs.
		public static void EmitComponentGetter(StringBuilder sb, ContextModel ctx, ComponentModel c)
		{
			string field = $"_matcher{c.TypeName}";
			sb.AppendLine($"\tstatic IMatcher<{ctx.Name}Entity> {field};");
			sb.AppendLine($"\tpublic static IMatcher<{ctx.Name}Entity> {c.TypeName}");
			sb.AppendLine("\t{");
			sb.AppendLine("\t\tget");
			sb.AppendLine("\t\t{");
			sb.AppendLine($"\t\t\tif ({field} == null)");
			sb.AppendLine("\t\t\t{");
			sb.AppendLine($"\t\t\t\tMatcher<{ctx.Name}Entity> m = (Matcher<{ctx.Name}Entity>)Matcher.AllOfIndices<{ctx.Name}Entity>({ctx.Name}ComponentsLookup.{c.TypeName});");
			sb.AppendLine($"\t\t\t\tm.componentNames = {ctx.Name}ComponentsLookup.componentNames;");
			sb.AppendLine($"\t\t\t\t{field} = m;");
			sb.AppendLine("\t\t\t}");
			sb.AppendLine($"\t\t\treturn {field};");
			sb.AppendLine("\t\t}");
			sb.AppendLine("\t}");
		}
	}

	// Per-context server replication bootstrap. Registers a snapshotter
	// that the runtime calls when a new player's client fires the Ready
	// event. The snapshotter walks every live entity, checks each
	// replicated component's HasX, and appends a Set op carrying the
	// current field values. Order is GetEntities() order then codegen
	// order — stable within a snapshot but not relied on by the client.
	//
	// User code instantiates this once per context on the server, usually
	// next to the matching {Ctx}ClientReplication on the client. Both are
	// no-ops on the wrong runtime side (RegisterSnapshotter / Subscribe
	// guard internally on IsServer), so unconditional construction is
	// fine if you don't want to branch.
	internal static class ServerReplicationTemplate
	{
		public static string Emit(ContextModel ctx, List<ComponentModel> replicated)
		{
			string entityType = $"{ctx.Name}Entity";
			string contextType = $"{ctx.Name}Context";

			StringBuilder sb = new();
			sb.AppendLine("// <auto-generated/> roblox-csharp-entities");
			sb.AppendLine("using System.Collections.Generic;");
			sb.AppendLine("using Entities;");
			sb.AppendLine();
			sb.AppendLine($"public sealed class {ctx.Name}ServerReplication");
			sb.AppendLine("{");
			sb.AppendLine($"\tprivate readonly {contextType} _context;");
			sb.AppendLine();
			sb.AppendLine($"\tpublic {ctx.Name}ServerReplication({contextType} context)");
			sb.AppendLine("\t{");
			sb.AppendLine("\t\t_context = context;");
			sb.AppendLine($"\t\tEntitiesReplication.RegisterSnapshotter(\"{ctx.Name}\", Snapshot);");
			sb.AppendLine("\t}");
			sb.AppendLine();

			// Snapshot — the late-join payload. Returns an ops array shaped
			// identically to a tick-time batch so the client dispatches it
			// through the same OnOps path.
			sb.AppendLine("\tprivate object[] Snapshot()");
			sb.AppendLine("\t{");
			sb.AppendLine("\t\tList<object> ops = new();");
			sb.AppendLine($"\t\t{entityType}[] entities = _context.GetEntities();");
			sb.AppendLine("\t\tfor (int i = 0; i < entities.Length; i++)");
			sb.AppendLine("\t\t{");
			sb.AppendLine($"\t\t\t{entityType} e = entities[i];");
			foreach (ComponentModel c in replicated)
			{
				string lookup = $"{ctx.Name}ComponentsLookup.{c.TypeName}";
				if (c.IsFlag)
				{
					sb.AppendLine($"\t\t\tif (e.Is{c.TypeName})");
					sb.AppendLine($"\t\t\t\tops.Add(new object[] {{ 0, {lookup}, e.creationIndex }});");
				}
				else
				{
					bool unwrap = c.Fields.Count == 1 && c.Fields[0].Name == "Value";
					sb.AppendLine($"\t\t\tif (e.Has{c.TypeName})");
					sb.AppendLine("\t\t\t{");
					if (unwrap)
					{
						// Single Value field → the entity exposes the unwrapped
						// value directly via e.{TypeName}, same shape as the
						// tick-time Set op.
						sb.AppendLine($"\t\t\t\tops.Add(new object[] {{ 0, {lookup}, e.creationIndex, e.{c.TypeName} }});");
					}
					else
					{
						// Multi-field or single non-Value → pull the component
						// instance, then unpack each field positionally.
						sb.AppendLine($"\t\t\t\t{c.FullName} comp = e.{c.TypeName};");
						string fieldArgs = string.Join(", ", c.Fields.Select(f => $"comp.{f.Name}"));
						sb.AppendLine($"\t\t\t\tops.Add(new object[] {{ 0, {lookup}, e.creationIndex, {fieldArgs} }});");
					}
					sb.AppendLine("\t\t\t}");
				}
			}
			sb.AppendLine("\t\t}");
			sb.AppendLine("\t\treturn ops.ToArray();");
			sb.AppendLine("\t}");
			sb.AppendLine("}");
			return sb.ToString();
		}
	}

	// Per-context client replication handler. Subscribes once to the
	// runtime's per-context RemoteEvent via EntitiesReplication.Subscribe
	// and dispatches each received op onto the local mirror entity.
	// EntitiesReplication.ShouldEmit() returns false on the client, so the
	// AddX/ReplaceX/RemoveX calls made here never echo back over the wire.
	//
	// Entity identity is the server's creationIndex carried as `serverId`
	// in every op. The handler keeps a dictionary mapping serverId →
	// local entity; new entities are constructed lazily on the first Set
	// op for an unknown serverId. Remove ops on unknown serverIds are
	// dropped — Roblox ordering across separate fires isn't guaranteed if
	// the user adds another RemoteEvent later, but within a single fire
	// the ops array is in server-emit order.
	//
	// Wire op shape (matches EntitiesReplication.luau):
	//   {opcode, componentIndex, entityId, ...fields}
	//   opcode 0 = Set, opcode 1 = Remove
	internal static class ClientReplicationTemplate
	{
		public static string Emit(ContextModel ctx, List<ComponentModel> replicated)
		{
			string entityType = $"{ctx.Name}Entity";
			string contextType = $"{ctx.Name}Context";

			StringBuilder sb = new();
			sb.AppendLine("// <auto-generated/> roblox-csharp-entities");
			sb.AppendLine("using System.Collections.Generic;");
			sb.AppendLine("using Entities;");
			sb.AppendLine();
			sb.AppendLine($"public sealed class {ctx.Name}ClientReplication");
			sb.AppendLine("{");
			sb.AppendLine($"\tprivate readonly {contextType} _context;");
			sb.AppendLine($"\tprivate readonly Dictionary<int, {entityType}> _byServerId = new();");
			sb.AppendLine();
			sb.AppendLine($"\tpublic {ctx.Name}ClientReplication({contextType} context)");
			sb.AppendLine("\t{");
			sb.AppendLine("\t\t_context = context;");
			sb.AppendLine($"\t\tEntitiesReplication.Subscribe(\"{ctx.Name}\", OnOps);");
			sb.AppendLine("\t}");
			sb.AppendLine();

			// `out var x` doesn't lower today (DeclarationExpressionSyntax
			// gap), so use the ContainsKey + indexer pattern; both have
			// transpiler support per alpha.28.
			sb.AppendLine($"\tprivate {entityType} GetOrCreate(int serverId)");
			sb.AppendLine("\t{");
			sb.AppendLine($"\t\tif (_byServerId.ContainsKey(serverId)) return _byServerId[serverId];");
			sb.AppendLine($"\t\t{entityType} created = _context.CreateEntity();");
			sb.AppendLine("\t\t_byServerId[serverId] = created;");
			sb.AppendLine("\t\treturn created;");
			sb.AppendLine("\t}");
			sb.AppendLine();

			// Receives a batch (server fires once per Heartbeat with every
			// op the tick produced). The cast pattern (object → object[])
			// mirrors how the transpiler lowers Lua tables to indexable
			// arrays in C#.
			sb.AppendLine("\tprivate void OnOps(object[] ops)");
			sb.AppendLine("\t{");
			sb.AppendLine("\t\tfor (int i = 0; i < ops.Length; i++)");
			sb.AppendLine("\t\t{");
			sb.AppendLine("\t\t\tobject[] op = (object[])ops[i];");
			sb.AppendLine("\t\t\tint opcode = (int)op[0];");
			sb.AppendLine("\t\t\tint compIndex = (int)op[1];");
			sb.AppendLine("\t\t\tint serverId = (int)op[2];");
			sb.AppendLine("\t\t\tif (opcode == 1) { ApplyRemove(compIndex, serverId); continue; }");
			sb.AppendLine("\t\t\tApplySet(compIndex, GetOrCreate(serverId), op);");
			sb.AppendLine("\t\t}");
			sb.AppendLine("\t}");
			sb.AppendLine();

			// Set dispatch — choose Add vs Replace by HasX so the user
			// sees idempotent application of a re-sent op (the server can
			// resend on late join / re-sync without the client desyncing).
			sb.AppendLine($"\tprivate void ApplySet(int compIndex, {entityType} e, object[] op)");
			sb.AppendLine("\t{");
			foreach (ComponentModel c in replicated)
			{
				sb.AppendLine($"\t\tif (compIndex == {ctx.Name}ComponentsLookup.{c.TypeName})");
				sb.AppendLine("\t\t{");
				if (c.IsFlag)
				{
					sb.AppendLine($"\t\t\te.Is{c.TypeName} = true;");
				}
				else
				{
					List<string> argList = new();
					for (int i = 0; i < c.Fields.Count; i++)
					{
						ComponentField f = c.Fields[i];
						// op[3] is first field, op[4] is second, etc.
						argList.Add($"({f.TypeFullName})op[{3 + i}]");
					}
					string args = string.Join(", ", argList);
					sb.AppendLine($"\t\t\tif (e.Has{c.TypeName}) e.Replace{c.TypeName}({args});");
					sb.AppendLine($"\t\t\telse e.Add{c.TypeName}({args});");
				}
				sb.AppendLine("\t\t\treturn;");
				sb.AppendLine("\t\t}");
			}
			sb.AppendLine("\t}");
			sb.AppendLine();

			sb.AppendLine("\tprivate void ApplyRemove(int compIndex, int serverId)");
			sb.AppendLine("\t{");
			sb.AppendLine("\t\tif (!_byServerId.ContainsKey(serverId)) return;");
			sb.AppendLine($"\t\t{entityType} e = _byServerId[serverId];");
			foreach (ComponentModel c in replicated)
			{
				sb.AppendLine($"\t\tif (compIndex == {ctx.Name}ComponentsLookup.{c.TypeName})");
				sb.AppendLine("\t\t{");
				if (c.IsFlag)
					sb.AppendLine($"\t\t\te.Is{c.TypeName} = false;");
				else
					sb.AppendLine($"\t\t\te.Remove{c.TypeName}();");
				sb.AppendLine("\t\t\treturn;");
				sb.AppendLine("\t\t}");
			}
			sb.AppendLine("\t}");
			sb.AppendLine("}");
			return sb.ToString();
		}
	}

	// One file per (context, component) pair — the navigable unit of the
	// codegen. Holds the entity property/methods + the matcher static
	// getter + (for [Unique]) the context-level singleton accessors. The
	// transpiler merges every partial back into one Luau class.
	internal static class ComponentTemplate
	{
		public static string Emit(ContextModel ctx, ComponentModel c)
		{
			StringBuilder sb = new();
			sb.AppendLine("// <auto-generated/> roblox-csharp-entities");
			sb.AppendLine("using Entities;");
			sb.AppendLine();

			sb.AppendLine($"public sealed partial class {ctx.Name}Entity");
			sb.AppendLine("{");
			string lookup = $"{ctx.Name}ComponentsLookup.{c.TypeName}";
			if (c.IsFlag) EntityTemplate.EmitFlag(sb, ctx, c, lookup);
			else EntityTemplate.EmitValue(sb, ctx, c, lookup);
			sb.AppendLine("}");

			sb.AppendLine();

			sb.AppendLine($"public sealed partial class {ctx.Name}Matcher");
			sb.AppendLine("{");
			MatcherTemplate.EmitComponentGetter(sb, ctx, c);
			sb.AppendLine("}");

			if (c.IsUnique)
			{
				sb.AppendLine();
				UniqueContextPartialTemplate.Emit(sb, ctx, c);
			}

			if (c.HasIndexedField)
			{
				sb.AppendLine();
				IndexContextPartialTemplate.Emit(sb, ctx, c);
			}

			return sb.ToString();
		}
	}

	// [EntityIndex] / [PrimaryEntityIndex] support — emitted as a
	// `partial class {Ctx}Context` block per indexed field. Each indexed
	// field contributes:
	//   • a private dict: `Dictionary<TKey, {Ctx}Entity>` for primary,
	//     `Dictionary<TKey, HashSet<{Ctx}Entity>>` for non-primary
	//   • a public lookup: `GetEntityWith{Component}(TKey)` returning the
	//     single entity (or null) for primary, or
	//     `GetEntitiesWith{Component}(TKey)` returning an IEnumerable for
	//     non-primary
	//   • internal `_Register{Component}` / `_Unregister{Component}` hooks
	//     called from the entity's AddX / ReplaceX / RemoveX / setter
	//     bodies to keep the dict in sync
	// If a component has more than one indexed field, the method/hook
	// names are disambiguated with the field name suffix.
	internal static class IndexContextPartialTemplate
	{
		public static void Emit(StringBuilder sb, ContextModel ctx, ComponentModel c)
		{
			string entityType = $"{ctx.Name}Entity";
			int indexedCount = 0;
			for (int i = 0; i < c.Fields.Count; i++) if (c.Fields[i].IsIndexed) indexedCount++;

			sb.AppendLine($"public sealed partial class {ctx.Name}Context");
			sb.AppendLine("{");
			foreach (ComponentField f in c.Fields)
			{
				if (!f.IsIndexed) continue;
				string suffix = indexedCount > 1 ? f.Name : "";
				string keyType = f.TypeFullName;
				string dictField = $"_{LowerFirst(c.TypeName)}By{f.Name}";

				if (f.IsPrimaryIndexed)
				{
					sb.AppendLine($"\tprivate System.Collections.Generic.Dictionary<{keyType}, {entityType}> {dictField} = new();");

					// Lookup — null when missing. Matches Entitas's
					// `GetEntityWith{Component}` shape.
					sb.AppendLine($"\tpublic {entityType} GetEntityWith{c.TypeName}{suffix}({keyType} key)");
					sb.AppendLine("\t{");
					sb.AppendLine($"\t\tif ({dictField}.ContainsKey(key)) return {dictField}[key];");
					sb.AppendLine("\t\treturn null;");
					sb.AppendLine("\t}");

					// Register — duplicate key on a different entity is a
					// hard error. Matches Entitas's primary-index guarantee:
					// one entity per key, conflicts surface immediately.
					sb.AppendLine($"\tinternal void _Register{c.TypeName}{suffix}({entityType} entity, {keyType} key)");
					sb.AppendLine("\t{");
					sb.AppendLine($"\t\tif ({dictField}.ContainsKey(key) && {dictField}[key] != entity)");
					sb.AppendLine($"\t\t\tthrow new System.Exception(\"PrimaryEntityIndex collision on {c.TypeName}.{f.Name} for key: \" + key);");
					sb.AppendLine($"\t\t{dictField}[key] = entity;");
					sb.AppendLine("\t}");

					sb.AppendLine($"\tinternal void _Unregister{c.TypeName}{suffix}({entityType} entity, {keyType} key)");
					sb.AppendLine("\t{");
					sb.AppendLine($"\t\tif ({dictField}.ContainsKey(key) && {dictField}[key] == entity) {dictField}.Remove(key);");
					sb.AppendLine("\t}");
				}
				else
				{
					sb.AppendLine($"\tprivate System.Collections.Generic.Dictionary<{keyType}, System.Collections.Generic.HashSet<{entityType}>> {dictField} = new();");

					// Lookup — empty enumerable when missing so callers can
					// foreach unconditionally.
					sb.AppendLine($"\tpublic System.Collections.Generic.IEnumerable<{entityType}> GetEntitiesWith{c.TypeName}{suffix}({keyType} key)");
					sb.AppendLine("\t{");
					sb.AppendLine($"\t\tif ({dictField}.ContainsKey(key)) return {dictField}[key];");
					sb.AppendLine($"\t\treturn System.Linq.Enumerable.Empty<{entityType}>();");
					sb.AppendLine("\t}");

					sb.AppendLine($"\tinternal void _Register{c.TypeName}{suffix}({entityType} entity, {keyType} key)");
					sb.AppendLine("\t{");
					sb.AppendLine($"\t\tSystem.Collections.Generic.HashSet<{entityType}> set;");
					sb.AppendLine($"\t\tif ({dictField}.ContainsKey(key)) {{ set = {dictField}[key]; }}");
					sb.AppendLine($"\t\telse {{ set = new System.Collections.Generic.HashSet<{entityType}>(); {dictField}[key] = set; }}");
					sb.AppendLine("\t\tset.Add(entity);");
					sb.AppendLine("\t}");

					sb.AppendLine($"\tinternal void _Unregister{c.TypeName}{suffix}({entityType} entity, {keyType} key)");
					sb.AppendLine("\t{");
					sb.AppendLine($"\t\tif ({dictField}.ContainsKey(key)) {dictField}[key].Remove(entity);");
					sb.AppendLine("\t}");
				}
			}
			sb.AppendLine("}");
		}

		private static string LowerFirst(string s)
			=> string.IsNullOrEmpty(s) ? s : char.ToLowerInvariant(s[0]) + s.Substring(1);
	}

	// [Unique] support — emitted as a `partial class {Ctx}Context` block
	// inside the per-component file. Each Unique component contributes:
	//   • a private `_{name}Entity` field
	//   • public `{lowerName}Entity` / `is{Name}` (flag) or `{Name}`/`has{Name}`
	//     (value) accessors that read through the singleton
	//   • `Set{Name}(...)` / `Unset{Name}()` lifecycle methods
	//   • internal `_Set{Name}Entity(this)` / `_Clear{Name}Entity()` hooks
	//     called from the entity's AddX / ReplaceX / RemoveX / setter
	//     bodies so user code that mutates via the entity API stays in sync
	//     with the context's singleton field. Duplicate assignment throws —
	//     "Unique" means one entity at a time, matching Entitas.
	internal static class UniqueContextPartialTemplate
	{
		public static void Emit(StringBuilder sb, ContextModel ctx, ComponentModel c)
		{
			string entityType = $"{ctx.Name}Entity";
			string field = $"_{LowerFirst(c.TypeName)}Entity";
			string entityProp = $"{LowerFirst(c.TypeName)}Entity";

			sb.AppendLine($"public sealed partial class {ctx.Name}Context");
			sb.AppendLine("{");
			sb.AppendLine($"\tprivate {entityType} {field};");
			sb.AppendLine($"\tpublic {entityType} {entityProp} {{ get {{ return {field}; }} }}");

			if (c.IsFlag)
			{
				// Flag: `isPlayer` returns true iff the holder still carries it.
				sb.AppendLine($"\tpublic bool is{c.TypeName} {{ get {{ return {field} != null && {field}.Is{c.TypeName}; }} }}");

				// Set: create-on-demand, then flip the flag. Idempotent —
				// re-call when already set is a no-op.
				sb.AppendLine($"\tpublic {entityType} Set{c.TypeName}()");
				sb.AppendLine("\t{");
				sb.AppendLine($"\t\tif ({field} == null) {field} = CreateEntity();");
				sb.AppendLine($"\t\t{field}.Is{c.TypeName} = true;");
				sb.AppendLine($"\t\treturn {field};");
				sb.AppendLine("\t}");

				// Unset: destroy the holder (Entitas convention — Unique
				// entities are conventionally single-component, so destroy
				// is the cleanest cleanup; user can call entity APIs
				// directly if they want more granular control).
				sb.AppendLine($"\tpublic void Unset{c.TypeName}()");
				sb.AppendLine("\t{");
				sb.AppendLine($"\t\tif ({field} != null)");
				sb.AppendLine("\t\t{");
				sb.AppendLine($"\t\t\t{field}.Destroy();");
				sb.AppendLine($"\t\t\t{field} = null;");
				sb.AppendLine("\t\t}");
				sb.AppendLine("\t}");
			}
			else
			{
				bool unwrap = c.Fields.Count == 1 && c.Fields[0].Name == "Value";

				// `has{Name}` mirrors the entity-side Has check, gated by
				// the singleton presence.
				sb.AppendLine($"\tpublic bool has{c.TypeName} {{ get {{ return {field} != null && {field}.Has{c.TypeName}; }} }}");

				if (unwrap)
				{
					// Single-Value: expose the unwrapped value directly so
					// `gameContext.Health` returns `int`, not the component.
					string valueType = c.Fields[0].TypeFullName;
					sb.AppendLine($"\tpublic {valueType} {c.TypeName} {{ get {{ return {field} != null ? {field}.{c.TypeName} : default({valueType}); }} }}");
				}
				else
				{
					sb.AppendLine($"\tpublic {c.FullName} {LowerFirst(c.TypeName)} {{ get {{ return {field} != null ? {field}.{c.TypeName} : null; }} }}");
				}

				// Set / Replace: factory + assignment. Same ctor-arg shape
				// as entity AddX so caller doesn't have to think about
				// whether the holder exists yet.
				string ctorArgs = string.Join(", ", c.Fields.Select(f => $"{f.TypeFullName} new{f.Name}"));
				string callArgs = string.Join(", ", c.Fields.Select(f => $"new{f.Name}"));
				sb.AppendLine($"\tpublic {entityType} Set{c.TypeName}({ctorArgs})");
				sb.AppendLine("\t{");
				sb.AppendLine($"\t\tif ({field} == null)");
				sb.AppendLine("\t\t{");
				sb.AppendLine($"\t\t\t{field} = CreateEntity();");
				sb.AppendLine($"\t\t\t{field}.Add{c.TypeName}({callArgs});");
				sb.AppendLine("\t\t}");
				sb.AppendLine("\t\telse");
				sb.AppendLine("\t\t{");
				sb.AppendLine($"\t\t\t{field}.Replace{c.TypeName}({callArgs});");
				sb.AppendLine("\t\t}");
				sb.AppendLine($"\t\treturn {field};");
				sb.AppendLine("\t}");

				sb.AppendLine($"\tpublic {entityType} Replace{c.TypeName}({ctorArgs}) {{ return Set{c.TypeName}({callArgs}); }}");

				sb.AppendLine($"\tpublic void Unset{c.TypeName}()");
				sb.AppendLine("\t{");
				sb.AppendLine($"\t\tif ({field} != null)");
				sb.AppendLine("\t\t{");
				sb.AppendLine($"\t\t\t{field}.Destroy();");
				sb.AppendLine($"\t\t\t{field} = null;");
				sb.AppendLine("\t\t}");
				sb.AppendLine("\t}");
			}

			// Internal hooks — invoked by codegen-emitted entity bodies so
			// that user code mutating via the entity API stays in sync.
			// `_Set` throws on conflicting holder to match Entitas's
			// "one entity at a time" guarantee.
			sb.AppendLine($"\tinternal void _Set{c.TypeName}Entity({entityType} entity)");
			sb.AppendLine("\t{");
			sb.AppendLine($"\t\tif ({field} != null && {field} != entity)");
			sb.AppendLine("\t\t{");
			sb.AppendLine($"\t\t\tthrow new System.Exception(\"Unique component {c.TypeName} is already assigned to a different entity.\");");
			sb.AppendLine("\t\t}");
			sb.AppendLine($"\t\t{field} = entity;");
			sb.AppendLine("\t}");
			sb.AppendLine($"\tinternal void _Clear{c.TypeName}Entity() {{ {field} = null; }}");
			sb.AppendLine("}");
		}

		private static string LowerFirst(string s)
			=> string.IsNullOrEmpty(s) ? s : char.ToLowerInvariant(s[0]) + s.Substring(1);
	}

	// Emits a tiny C# class file for the synthesized {Name}Changed flag
	// of a [Watched] component. No attributes — the codegen synthesizes
	// the matching ComponentModel directly and never re-discovers this
	// class via attribute scan. The class only needs to exist so user
	// code can reference the type (`entity.Is{Name}Changed`) and so the
	// flag's static-singleton instantiation compiles.
	internal static class WatchedFlagClassTemplate
	{
		public static string Emit(ComponentModel source)
		{
			StringBuilder sb = new();
			sb.AppendLine("// <auto-generated/> roblox-csharp-entities");
			sb.AppendLine("using Entities;");
			sb.AppendLine();
			if (!string.IsNullOrEmpty(source.NamespaceName))
			{
				sb.AppendLine($"namespace {source.NamespaceName}");
				sb.AppendLine("{");
				sb.AppendLine($"\tpublic class {source.TypeName}Changed : IComponent {{ }}");
				sb.AppendLine("}");
			}
			else
			{
				sb.AppendLine($"public class {source.TypeName}Changed : IComponent {{ }}");
			}
			return sb.ToString();
		}
	}

	// Emits a per-context cleanup system that clears every Changed flag
	// at end of frame. User adds it to the tail of their feature
	// pipeline so reactive systems consuming `*Changed` see one-frame
	// signals. One system per context; one loop per [Watched] component.
	internal static class WatchedCleanupSystemTemplate
	{
		public static string Emit(ContextModel ctx, List<ComponentModel> watchedSources)
		{
			string entityType = $"{ctx.Name}Entity";

			StringBuilder sb = new();
			sb.AppendLine("// <auto-generated/> roblox-csharp-entities");
			sb.AppendLine("using Entities;");
			sb.AppendLine();
			sb.AppendLine($"public sealed class {ctx.Name}WatchedCleanupSystem : ICleanupSystem");
			sb.AppendLine("{");
			foreach (ComponentModel c in watchedSources)
			{
				sb.AppendLine($"\tprivate readonly IGroup<{entityType}> _{LowerFirst(c.TypeName)}Changed;");
			}
			sb.AppendLine();
			sb.AppendLine($"\tpublic {ctx.Name}WatchedCleanupSystem({ctx.Name}Context context)");
			sb.AppendLine("\t{");
			foreach (ComponentModel c in watchedSources)
			{
				sb.AppendLine($"\t\t_{LowerFirst(c.TypeName)}Changed = context.GetGroup({ctx.Name}Matcher");
				sb.AppendLine("\t\t\t.AllOf(");
				sb.AppendLine($"\t\t\t\t{ctx.Name}Matcher.{c.TypeName}Changed));");
			}
			sb.AppendLine("\t}");
			sb.AppendLine();
			sb.AppendLine("\tpublic void Cleanup()");
			sb.AppendLine("\t{");
			foreach (ComponentModel c in watchedSources)
			{
				sb.AppendLine($"\t\tforeach ({entityType} e in _{LowerFirst(c.TypeName)}Changed.GetEntities())");
				sb.AppendLine($"\t\t\te.Is{c.TypeName}Changed = false;");
			}
			sb.AppendLine("\t}");
			sb.AppendLine("}");
			return sb.ToString();
		}

		private static string LowerFirst(string s)
			=> string.IsNullOrEmpty(s) ? s : char.ToLowerInvariant(s[0]) + s.Substring(1);
	}

	internal static class ContextsTemplate
	{
		public static string Emit(List<ContextModel> contexts, List<string> postConstructors)
		{
			StringBuilder sb = new();
			sb.AppendLine("// <auto-generated/> roblox-csharp-entities");
			sb.AppendLine("using Entities;");
			sb.AppendLine();
			sb.AppendLine("public partial class Contexts : IContexts");
			sb.AppendLine("{");
			sb.AppendLine("\tpublic static Contexts sharedInstance");
			sb.AppendLine("\t{");
			sb.AppendLine("\t\tget");
			sb.AppendLine("\t\t{");
			sb.AppendLine("\t\t\tif (_sharedInstance == null) _sharedInstance = new Contexts();");
			sb.AppendLine("\t\t\treturn _sharedInstance;");
			sb.AppendLine("\t\t}");
			sb.AppendLine("\t\tset { _sharedInstance = value; }");
			sb.AppendLine("\t}");
			sb.AppendLine("\tstatic Contexts _sharedInstance;");
			sb.AppendLine();
			foreach (ContextModel ctx in contexts)
			{
				sb.AppendLine($"\tpublic {ctx.Name}Context {LowerFirst(ctx.Name)} {{ get; set; }}");
			}
			sb.AppendLine();
			sb.AppendLine("\tpublic IContext[] allContexts");
			sb.AppendLine("\t{");
			sb.AppendLine($"\t\tget {{ return new IContext[] {{ {string.Join(", ", contexts.Select(c => LowerFirst(c.Name)))} }}; }}");
			sb.AppendLine("\t}");
			sb.AppendLine();
			sb.AppendLine("\tpublic Contexts()");
			sb.AppendLine("\t{");
			foreach (ContextModel ctx in contexts)
			{
				sb.AppendLine($"\t\t{LowerFirst(ctx.Name)} = new {ctx.Name}Context();");
			}
			// [PostConstructor] tail-calls. Run after every per-context
			// field is wired so user code can register entity indices,
			// etc. without ordering surprises.
			if (postConstructors != null)
			{
				foreach (string name in postConstructors)
				{
					sb.AppendLine($"\t\t{name}();");
				}
			}
			sb.AppendLine("\t}");
			sb.AppendLine();
			sb.AppendLine("\tpublic void Reset()");
			sb.AppendLine("\t{");
			sb.AppendLine("\t\tIContext[] all = allContexts;");
			sb.AppendLine("\t\tfor (int i = 0; i < all.Length; i++) all[i].Reset();");
			sb.AppendLine("\t}");
			sb.AppendLine("}");
			return sb.ToString();
		}

		private static string LowerFirst(string s)
			=> string.IsNullOrEmpty(s) ? s : char.ToLowerInvariant(s[0]) + s.Substring(1);
	}
}
