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

namespace RobloxCSharp.Extensions.Entitas
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
	public sealed class EntitasExtension : IRobloxCSharpExtension
	{
		private const string PluginName = "Entitas";
		private const string GeneratedFolder = "Generated";

		public string Name => "Entitas.Codegen";

		public void PreSourceDiscovery(string srcDir, IReadOnlyList<Plugin> plugins, DiagnosticBag diagnostics)
		{
			Plugin self = plugins.FirstOrDefault(p => string.Equals(p.Name, PluginName, StringComparison.Ordinal));
			if (self is null) return;

			string stubsDir = Path.Combine(self.RootDir, "stubs");
			List<SyntaxTree> trees = new();
			AppendCsFiles(srcDir, trees);
			AppendCsFiles(stubsDir, trees);

			Compilation compilation = CSharpCompilation.Create(
				assemblyName: "Entitas.Codegen.Scan",
				syntaxTrees: trees,
				references: BuildReferences(),
				options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

			INamedTypeSymbol componentInterface = compilation.GetTypeByMetadataName("Entitas.IComponent");
			INamedTypeSymbol contextAttribute = compilation.GetTypeByMetadataName("Entitas.CodeGeneration.Attributes.ContextAttribute");
			if (componentInterface is null || contextAttribute is null) return;

			Dictionary<string, ContextModel> byContext = new(StringComparer.Ordinal);

			foreach (SyntaxTree tree in trees)
			{
				// Skip our own stubs; we only want user code.
				if (tree.FilePath.StartsWith(stubsDir, StringComparison.OrdinalIgnoreCase)) continue;

				SemanticModel model = compilation.GetSemanticModel(tree);
				foreach (ClassDeclarationSyntax classDecl in tree.GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>())
				{
					INamedTypeSymbol typeSymbol = model.GetDeclaredSymbol(classDecl) as INamedTypeSymbol;
					if (typeSymbol is null) continue;
					if (typeSymbol.IsAbstract) continue;
					if (!ImplementsInterface(typeSymbol, componentInterface)) continue;

					foreach (AttributeData attr in typeSymbol.GetAttributes())
					{
						if (!InheritsFrom(attr.AttributeClass, contextAttribute)) continue;
						string contextName = ExtractContextName(attr);
						if (string.IsNullOrEmpty(contextName)) continue;

						if (!byContext.TryGetValue(contextName, out ContextModel ctx))
						{
							ctx = new ContextModel(contextName);
							byContext[contextName] = ctx;
						}
						ctx.Components.Add(new ComponentModel(typeSymbol));
					}
				}
			}

			if (byContext.Count == 0) return;

			string genDir = Path.Combine(srcDir, GeneratedFolder);
			Directory.CreateDirectory(genDir);
			string componentsDir = Path.Combine(genDir, "Components");
			Directory.CreateDirectory(componentsDir);

			HashSet<string> expectedComponentFiles = new(StringComparer.OrdinalIgnoreCase);

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
			}

			WriteFile(genDir, "Contexts.cs", ContextsTemplate.Emit(byContext.Values.OrderBy(c => c.Name, StringComparer.Ordinal).ToList()));

			// Reap stale per-component files (component renamed, deleted,
			// attribute removed) so the codegen stays idempotent.
			foreach (string existing in Directory.EnumerateFiles(componentsDir, "*.cs"))
			{
				string name = Path.GetFileName(existing);
				if (!expectedComponentFiles.Contains(name)) File.Delete(existing);
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
			// name minus the "Attribute" suffix, which matches Entitas's
			// convention 1:1.
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

	internal sealed class ComponentModel
	{
		public INamedTypeSymbol Symbol { get; }
		public string TypeName => Symbol.Name;
		public string FullName { get; }
		public List<ComponentField> Fields { get; }
		public bool IsFlag => Fields.Count == 0;

		public ComponentModel(INamedTypeSymbol symbol)
		{
			Symbol = symbol;
			FullName = symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
			Fields = new List<ComponentField>();
			foreach (ISymbol member in symbol.GetMembers())
			{
				if (member is IFieldSymbol field && field.DeclaredAccessibility == Accessibility.Public && !field.IsStatic && !field.IsConst)
				{
					Fields.Add(new ComponentField(field.Name, field.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)));
				}
				// Property-shaped values (`public int Value { get; set; }`) are
				// not the conventional Entitas shape — frozen-feast uses plain
				// fields everywhere — so we keep this strict for the alpha and
				// flag it for follow-up if a user reports needing them.
			}
		}
	}

	internal readonly struct ComponentField
	{
		public string Name { get; }
		public string TypeFullName { get; }
		public ComponentField(string name, string typeFullName) { Name = name; TypeFullName = typeFullName; }
	}

	// ----------------------------------------------------------------
	// Code-gen templates
	// ----------------------------------------------------------------

	internal static class ComponentsLookupTemplate
	{
		public static string Emit(ContextModel ctx)
		{
			StringBuilder sb = new();
			sb.AppendLine("// <auto-generated/> roblox-csharp-entitas");
			sb.AppendLine("using Entitas;");
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
			foreach (ComponentModel c in ctx.Components) sb.AppendLine($"\t\ttypeof({c.FullName}),");
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
			sb.AppendLine("// <auto-generated/> roblox-csharp-entitas");
			sb.AppendLine("using Entitas;");
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
		// Base partial — just the shell with the inheritance line. The
		// per-component property + methods land in Generated/Components/
		// {Ctx}.{Component}.cs and merge into this class via partials.
		public static string Emit(ContextModel ctx)
		{
			StringBuilder sb = new();
			sb.AppendLine("// <auto-generated/> roblox-csharp-entitas");
			sb.AppendLine("using Entitas;");
			sb.AppendLine();
			sb.AppendLine($"public sealed partial class {ctx.Name}Entity : Entity {{ }}");
			return sb.ToString();
		}

		internal static void EmitFlag(StringBuilder sb, ComponentModel c, string lookup)
		{
			// Flag → `entity.Is{Name}` (PascalCase, gettable+settable bool).
			// Setter swaps the shared singleton instance in/out so we don't
			// allocate per flip.
			sb.AppendLine($"\tstatic readonly {c.FullName} _{c.TypeName}Component = new();");
			sb.AppendLine($"\tpublic bool Is{c.TypeName}");
			sb.AppendLine("\t{");
			sb.AppendLine($"\t\tget {{ return HasComponent({lookup}); }}");
			sb.AppendLine("\t\tset");
			sb.AppendLine("\t\t{");
			sb.AppendLine($"\t\t\tif (value != Is{c.TypeName})");
			sb.AppendLine("\t\t\t{");
			sb.AppendLine($"\t\t\t\tif (value) AddComponent({lookup}, _{c.TypeName}Component);");
			sb.AppendLine($"\t\t\t\telse RemoveComponent({lookup});");
			sb.AppendLine("\t\t\t}");
			sb.AppendLine("\t\t}");
			sb.AppendLine("\t}");
		}

		internal static void EmitValue(StringBuilder sb, ComponentModel c, string lookup)
		{
			string ctorArgs = string.Join(", ", c.Fields.Select(f => $"{f.TypeFullName} new{f.Name}"));
			string fieldAssigns = string.Join(" ", c.Fields.Select(f => $"component.{f.Name} = new{f.Name};"));

			// Single field named `Value` → unwrap to the field's type:
			//   `entity.Health` returns `int`, not the Health component.
			// Anything else (multi-field, or single non-`Value` field) →
			// return the component instance so `entity.Position.X` works.
			bool unwrap = c.Fields.Count == 1 && c.Fields[0].Name == "Value";

			if (unwrap)
			{
				// Setter routes through CreateComponent<T> so the new
				// instance comes off the pool when one's available.
				string valueType = c.Fields[0].TypeFullName;
				sb.AppendLine($"\tpublic {valueType} {c.TypeName}");
				sb.AppendLine("\t{");
				sb.AppendLine($"\t\tget {{ return (({c.FullName})GetComponent({lookup})).Value; }}");
				sb.AppendLine("\t\tset");
				sb.AppendLine("\t\t{");
				sb.AppendLine($"\t\t\t{c.FullName} component = CreateComponent<{c.FullName}>({lookup});");
				sb.AppendLine($"\t\t\tcomponent.Value = value;");
				sb.AppendLine($"\t\t\tReplaceComponent({lookup}, component);");
				sb.AppendLine("\t\t}");
				sb.AppendLine("\t}");
			}
			else
			{
				sb.AppendLine($"\tpublic {c.FullName} {c.TypeName} {{ get {{ return ({c.FullName})GetComponent({lookup}); }} }}");
			}
			sb.AppendLine($"\tpublic bool Has{c.TypeName} {{ get {{ return HasComponent({lookup}); }} }}");

			// AddX/ReplaceX now pull from the component pool when one's
			// available. Pool semantics match Entitas-1.14 — the recycled
			// instance has whatever state was on it before; the field
			// assignments below overwrite anything we care about.
			sb.AppendLine();
			sb.AppendLine($"\tpublic {c.FullName} Add{c.TypeName}({ctorArgs})");
			sb.AppendLine("\t{");
			sb.AppendLine($"\t\t{c.FullName} component = CreateComponent<{c.FullName}>({lookup});");
			sb.AppendLine($"\t\t{fieldAssigns}");
			sb.AppendLine($"\t\tAddComponent({lookup}, component);");
			sb.AppendLine("\t\treturn component;");
			sb.AppendLine("\t}");

			sb.AppendLine();
			sb.AppendLine($"\tpublic {c.FullName} Replace{c.TypeName}({ctorArgs})");
			sb.AppendLine("\t{");
			sb.AppendLine($"\t\t{c.FullName} component = CreateComponent<{c.FullName}>({lookup});");
			sb.AppendLine($"\t\t{fieldAssigns}");
			sb.AppendLine($"\t\tReplaceComponent({lookup}, component);");
			sb.AppendLine("\t\treturn component;");
			sb.AppendLine("\t}");

			sb.AppendLine();
			sb.AppendLine($"\tpublic void Remove{c.TypeName}()");
			sb.AppendLine("\t{");
			sb.AppendLine($"\t\tRemoveComponent({lookup});");
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
			sb.AppendLine("// <auto-generated/> roblox-csharp-entitas");
			sb.AppendLine("using Entitas;");
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

	// One file per (context, component) pair — the navigable unit of the
	// codegen. Holds the entity property/methods + the matcher static
	// getter for that single component, both as `partial class` members.
	// The transpiler merges every partial back into one Luau class.
	internal static class ComponentTemplate
	{
		public static string Emit(ContextModel ctx, ComponentModel c)
		{
			StringBuilder sb = new();
			sb.AppendLine("// <auto-generated/> roblox-csharp-entitas");
			sb.AppendLine("using Entitas;");
			sb.AppendLine();

			sb.AppendLine($"public sealed partial class {ctx.Name}Entity");
			sb.AppendLine("{");
			string lookup = $"{ctx.Name}ComponentsLookup.{c.TypeName}";
			if (c.IsFlag) EntityTemplate.EmitFlag(sb, c, lookup);
			else EntityTemplate.EmitValue(sb, c, lookup);
			sb.AppendLine("}");

			sb.AppendLine();

			sb.AppendLine($"public sealed partial class {ctx.Name}Matcher");
			sb.AppendLine("{");
			MatcherTemplate.EmitComponentGetter(sb, ctx, c);
			sb.AppendLine("}");

			return sb.ToString();
		}
	}

	internal static class ContextsTemplate
	{
		public static string Emit(List<ContextModel> contexts)
		{
			StringBuilder sb = new();
			sb.AppendLine("// <auto-generated/> roblox-csharp-entitas");
			sb.AppendLine("using Entitas;");
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
