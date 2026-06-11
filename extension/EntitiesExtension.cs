using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
	// This file is the orchestrator only â€” the discovery model, the
	// per-template emitters, and the symbol/file helpers live in
	// Discovery/ and Templates/ alongside it.
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
			SymbolHelpers.AppendCsFiles(srcDir, trees);
			SymbolHelpers.AppendCsFiles(stubsDir, trees);

			Compilation compilation = CSharpCompilation.Create(
				assemblyName: "Entities.Codegen.Scan",
				syntaxTrees: trees,
				references: SymbolHelpers.BuildReferences(),
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

			// [PostConstructor] scan â€” walk every `partial class Contexts`
			// in user code, collect names of methods carrying the
			// attribute. ContextsTemplate appends these as trailing calls
			// in the generated Contexts() ctor so user-side index
			// registration etc. runs after every per-context field is
			// wired.
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

					if (!SymbolHelpers.ImplementsInterface(typeSymbol, attrs.Component)) continue;

					foreach (AttributeData attr in typeSymbol.GetAttributes())
					{
						if (!SymbolHelpers.InheritsFrom(attr.AttributeClass, attrs.Context)) continue;

						// Abstract components are skipped on purpose (no
						// entity can carry them) — but a [Context]-tagged
						// abstract class means the user expected codegen,
						// so say so instead of generating nothing silently.
						if (typeSymbol.IsAbstract)
						{
							diagnostics.Warning("ENT0003",
								$"abstract component '{typeSymbol.Name}' is skipped by codegen — "
								+ "only concrete IComponent classes generate accessors.");
							break;
						}
						string contextName = SymbolHelpers.ExtractContextName(attr);
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

			ComponentValidation.Run(byContext, diagnostics);

			string genDir = Path.Combine(srcDir, GeneratedFolder);
			string componentsDir = Path.Combine(genDir, "Components");
			string watchedDir = Path.Combine(genDir, "Watched");
			string commandsDir = Path.Combine(genDir, "Commands");

			HashSet<string> expectedRootFiles = new(StringComparer.OrdinalIgnoreCase);
			HashSet<string> expectedComponentFiles = new(StringComparer.OrdinalIgnoreCase);
			HashSet<string> expectedWatchedFiles = new(StringComparer.OrdinalIgnoreCase);
			HashSet<string> expectedCommandsFiles = new(StringComparer.OrdinalIgnoreCase);

			if (byContext.Count > 0)
			{
				Directory.CreateDirectory(genDir);
				Directory.CreateDirectory(componentsDir);

				// [Watched] synthesis â€” for every [Watched] component, append
				// a {Name}Changed flag ComponentModel to the same context and
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
						SymbolHelpers.WriteFile(watchedDir, fileName, WatchedFlagClassTemplate.Emit(src));
						expectedWatchedFiles.Add(fileName);
					}
				}

				// Command + OriginUserId synthesis â€” injected into every
				// context unconditionally. The Command flag is the user-
				// facing clientâ†’server ship trigger (`entity.IsCommand =
				// true`); OriginUserId is the server-attached trust marker
				// carrying Player.UserId. Both class files live globally
				// (no namespace) so every context's lookup shares one C#
				// type; the files are emitted exactly once. Contexts that
				// never use commands pay nothing at runtime â€” the wire is
				// lazy and the per-frame drain is empty.
				Directory.CreateDirectory(commandsDir);
				const string originUserIdFileName = "OriginUserId.cs";
				const string commandFlagFileName = "Command.cs";
				SymbolHelpers.WriteFile(commandsDir, originUserIdFileName, OriginUserIdClassTemplate.Emit());
				SymbolHelpers.WriteFile(commandsDir, commandFlagFileName, CommandFlagClassTemplate.Emit());
				expectedCommandsFiles.Add(originUserIdFileName);
				expectedCommandsFiles.Add(commandFlagFileName);

				foreach (ContextModel ctx in byContext.Values)
				{
					ctx.Components.Add(ComponentModel.CreateCommandFlag());
					ctx.Components.Add(ComponentModel.CreateOriginUserId());
				}

				foreach (ContextModel ctx in byContext.Values)
				{
					// Stable componentIndex across builds. Type names are
					// unique within a context today (the lookup template
					// emits `public const int {TypeName}` which would
					// collide on duplicates), so type-name alone gives a
					// total order. The namespace secondary key is
					// defensive â€” if the lookup template ever emits
					// namespace-qualified consts, the order stays
					// well-defined under name collisions without an extra
					// migration. This sort is what BuildDigest hashes,
					// so it's also the wire contract.
					ctx.Components.Sort((a, b) =>
					{
						int n = string.Compare(a.NamespaceName ?? "", b.NamespaceName ?? "", StringComparison.Ordinal);
						return n != 0 ? n : string.Compare(a.TypeName, b.TypeName, StringComparison.Ordinal);
					});
					SymbolHelpers.WriteFile(genDir, $"{ctx.Name}ComponentsLookup.cs", ComponentsLookupTemplate.Emit(ctx));
					SymbolHelpers.WriteFile(genDir, $"{ctx.Name}Context.cs", ContextTemplate.Emit(ctx));
					SymbolHelpers.WriteFile(genDir, $"{ctx.Name}Entity.cs", EntityTemplate.Emit(ctx));
					SymbolHelpers.WriteFile(genDir, $"{ctx.Name}Matcher.cs", MatcherTemplate.Emit(ctx));
					expectedRootFiles.Add($"{ctx.Name}ComponentsLookup.cs");
					expectedRootFiles.Add($"{ctx.Name}Context.cs");
					expectedRootFiles.Add($"{ctx.Name}Entity.cs");
					expectedRootFiles.Add($"{ctx.Name}Matcher.cs");

					// Per-component partial â€” one file per component holds the
					// entity property/methods + the matcher static getter +
					// (for [Unique] / [EntityIndex]) the context-side state.
					// The transpiler merges every `partial class GameEntity`
					// across these files back into one Luau metatable-class.
					foreach (ComponentModel c in ctx.Components)
					{
						string fileName = $"{ctx.Name}.{c.TypeName}.cs";
						SymbolHelpers.WriteFile(componentsDir, fileName, ComponentTemplate.Emit(ctx, c));
						expectedComponentFiles.Add(fileName);

						// Separate hooks-interface file. Kept in its own .cs so
						// the per-component partial file stays partial-only â€” a
						// partial-only file emits no Luau, while a file mixing
						// partials + a real declaration would render the
						// partials as null placeholders alongside the
						// interface. The cast on the entity side dispatches
						// through CS.as by name, so this file is reference-only
						// at runtime.
						if (c.IsUnique || c.HasIndexedField)
						{
							string hooksFile = $"{ctx.Name}.{c.TypeName}.Hooks.cs";
							System.Text.StringBuilder sb = new();
							sb.AppendLine("// <auto-generated/> roblox-csharp-entities");
							sb.AppendLine("using Entities;");
							sb.AppendLine();
							ContextHooksInterfaceTemplate.Emit(sb, ctx, c);
							SymbolHelpers.WriteFile(componentsDir, hooksFile, sb.ToString());
							expectedComponentFiles.Add(hooksFile);
						}
					}

					// Replication â€” codegen-emitted entity AddX/ReplaceX/RemoveX
					// bodies enqueue into the per-context buffer in
					// EntitiesReplication (the call is in EntityTemplate). Two
					// companion files per context:
					//   {Ctx}ServerReplication â€” registers a snapshotter for
					//     late-join (walks every live entity + replicated
					//     component, builds a Set-op batch).
					//   {Ctx}ClientReplication â€” subscribes once and dispatches
					//     received ops by componentIndex onto the local entity.
					// When no component is replicated we just skip emit; the
					// unified sweep below reaps any stranded pair.
					List<ComponentModel> replicated = ctx.Components.Where(c => c.IsReplicated).ToList();
					if (replicated.Count > 0)
					{
						SymbolHelpers.WriteFile(genDir, $"{ctx.Name}ServerReplication.cs", ServerReplicationTemplate.Emit(ctx, replicated));
						SymbolHelpers.WriteFile(genDir, $"{ctx.Name}ClientReplication.cs", ClientReplicationTemplate.Emit(ctx, replicated));
						expectedRootFiles.Add($"{ctx.Name}ServerReplication.cs");
						expectedRootFiles.Add($"{ctx.Name}ClientReplication.cs");
					}

					// Commands â€” emitted unconditionally per context. The
					// client trigger is `entity.IsCommand = true`; the
					// setter tail marks the entity in the per-context
					// pending set, and the heartbeat drain ships the
					// entity's complete component set. Server receives,
					// spawns one entity per clientLocalId, applies every
					// carried component, and tail-attaches OriginUserId
					// from the Player.
					//
					// Pair per context:
					//   {Ctx}ClientCommandSender â€” registers a shipper
					//     fn with EntitiesReplication. The shipper walks
					//     entity.GetComponentIndices() and dispatches
					//     per index.
					//   {Ctx}ServerCommandReceiver â€” registers a
					//     receiver fn that decodes ops by index and
					//     applies them onto a fresh entity.
					//
					// Contexts that never use commands instantiate
					// neither and pay nothing at runtime â€” RemoteEvents
					// stay uncreated, pending sets stay empty.
					SymbolHelpers.WriteFile(genDir, $"{ctx.Name}ClientCommandSender.cs", ClientCommandSenderTemplate.Emit(ctx));
					SymbolHelpers.WriteFile(genDir, $"{ctx.Name}ServerCommandReceiver.cs", ServerCommandReceiverTemplate.Emit(ctx));
					expectedRootFiles.Add($"{ctx.Name}ClientCommandSender.cs");
					expectedRootFiles.Add($"{ctx.Name}ServerCommandReceiver.cs");

					// [Watched] cleanup â€” emit per context when any component
					// in the context is [Watched]. The user adds it to the
					// tail of their feature pipeline; it clears every Changed
					// flag at end of frame so reactive systems see one-frame
					// signals.
					List<ComponentModel> watchedSources = ctx.Components
						.Where(c => c.IsWatched && !c.IsSynthesizedChangedFlag)
						.ToList();
					if (watchedSources.Count > 0)
					{
						SymbolHelpers.WriteFile(genDir, $"{ctx.Name}WatchedCleanupSystem.cs",
							WatchedCleanupSystemTemplate.Emit(ctx, watchedSources));
						expectedRootFiles.Add($"{ctx.Name}WatchedCleanupSystem.cs");
					}
				}

				SymbolHelpers.WriteFile(genDir, "Contexts.cs", ContextsTemplate.Emit(byContext.Values.OrderBy(c => c.Name, StringComparer.Ordinal).ToList(), postConstructors));
				expectedRootFiles.Add("Contexts.cs");
			}

			// Unified sweep â€” reap any *.cs in Generated/, Generated/Components/,
			// or Generated/Watched/ that this scan didn't (re)produce. Runs
			// even when byContext.Count == 0 so removing the last
			// [Context]-tagged component â€” or commenting one out â€” wipes the
			// tree cleanly. Also handles a whole context disappearing (orphans
			// {Ctx}Context.cs / {Ctx}Entity.cs / {Ctx}Matcher.cs / etc.) and
			// subsumes the prior-slice migration deletes for the renamed
			// Replication.cs / ClientMirror.cs / ClientMirror.client.cs
			// shapes â€” none of those names ever land in expectedRootFiles.
			if (Directory.Exists(componentsDir))
			{
				foreach (string existing in Directory.EnumerateFiles(componentsDir, "*.cs"))
				{
					string name = Path.GetFileName(existing);
					if (!expectedComponentFiles.Contains(name)) File.Delete(existing);
				}
			}
			if (Directory.Exists(watchedDir))
			{
				foreach (string existing in Directory.EnumerateFiles(watchedDir, "*.cs"))
				{
					string name = Path.GetFileName(existing);
					if (!expectedWatchedFiles.Contains(name)) File.Delete(existing);
				}
			}
			if (Directory.Exists(commandsDir))
			{
				foreach (string existing in Directory.EnumerateFiles(commandsDir, "*.cs"))
				{
					string name = Path.GetFileName(existing);
					if (!expectedCommandsFiles.Contains(name)) File.Delete(existing);
				}
			}
			if (Directory.Exists(genDir))
			{
				foreach (string existing in Directory.EnumerateFiles(genDir, "*.cs"))
				{
					string name = Path.GetFileName(existing);
					if (!expectedRootFiles.Contains(name)) File.Delete(existing);
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
	}
}
