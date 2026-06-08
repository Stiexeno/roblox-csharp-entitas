using System.Text.Json.Nodes;
using RobloxCSharp;
using RobloxCSharp.Common.Diagnostics;
using RobloxCSharp.Compiler;
using RobloxCSharp.Plugins;
using RobloxCSharp.Renderer;
using RobloxCSharp.Rojo;
using RobloxCSharp.Transformer;
using RobloxCSharp.Transformer.AST;
using RobloxCSharp.Transformer.Extensibility;
using RobloxCSharp.Extensions.Entities;

namespace Entities.Tests
{
	// Per-test temp directory + helpers. PreSourceDiscovery does disk
	// IO (Generated/*.cs are real files), so the tests need a real
	// project layout — same approach as PluginsEndToEndTests in the
	// main repo.
	internal static class TestHarness
	{
		private static readonly string FixturesRoot =
			Path.Combine(Path.GetTempPath(), "RobloxCSharpEntitiesTests");

		// Locate the plugin root by walking up from the test assembly.
		// The plugin's manifest.json sits next to stubs/ and runtime/ at
		// the repo root.
		private static readonly Lazy<string> _pluginRoot = new(() =>
		{
			string dir = AppContext.BaseDirectory;
			while (dir is not null)
			{
				if (File.Exists(Path.Combine(dir, "manifest.json"))
					&& Directory.Exists(Path.Combine(dir, "stubs")))
					return dir;
				dir = Directory.GetParent(dir)?.FullName;
			}
			throw new InvalidOperationException(
				"Could not locate roblox-csharp-entities plugin root from " + AppContext.BaseDirectory);
		});

		public static string PluginRoot => _pluginRoot.Value;

		public sealed class Project
		{
			public string Root { get; init; }
			public string SrcDir { get; init; }
			public string OutDir { get; init; }
			public string GeneratedDir => Path.Combine(SrcDir, "Generated");
			public IReadOnlyList<Plugin> Plugins { get; set; }
			public DiagnosticBag Diagnostics { get; set; }
		}

		public static Project Setup(string testName)
		{
			string root = Path.Combine(FixturesRoot, testName);
			if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
			Directory.CreateDirectory(root);

			string srcDir = Path.Combine(root, "src");
			string outDir = Path.Combine(root, "out");
			Directory.CreateDirectory(srcDir);

			// Default Rojo tree — Shared / Plugins under ReplicatedStorage.
			JsonObject tree = new()
			{
				["$className"] = "DataModel",
				["ReplicatedStorage"] = new JsonObject
				{
					["Shared"] = new JsonObject { ["$path"] = Path.Combine("out", "shared") },
					["Plugins"] = new JsonObject { ["$path"] = Path.Combine("out", "plugins") },
				},
			};
			JsonObject projectFile = new()
			{
				["name"] = testName,
				["tree"] = tree,
			};
			File.WriteAllText(Path.Combine(root, "default.project.json"), projectFile.ToJsonString());

			// Mount the Entities plugin into the temp project — copy the
			// manifest + stubs so PluginLoader sees a real plugin layout.
			// Runtime is optional for codegen-only tests; copy it lazily.
			string pluginDest = Path.Combine(root, "plugins", "Entities");
			Directory.CreateDirectory(Path.Combine(pluginDest, "stubs"));
			File.Copy(
				Path.Combine(PluginRoot, "manifest.json"),
				Path.Combine(pluginDest, "manifest.json"));
			CopyDirectory(
				Path.Combine(PluginRoot, "stubs"),
				Path.Combine(pluginDest, "stubs"));

			return new Project { Root = root, SrcDir = srcDir, OutDir = outDir };
		}

		// Drop a user source file into src/ at the given relative path.
		// The path is relative to src/ — pass "MyGame/Combat.cs" to land
		// at src/MyGame/Combat.cs.
		public static void WriteSource(Project p, string relativePath, string source)
		{
			string full = Path.Combine(p.SrcDir, relativePath);
			Directory.CreateDirectory(Path.GetDirectoryName(full)!);
			File.WriteAllText(full, source);
		}

		// Runs the codegen step — same shape as the CLI's RunRojoProject
		// before source enumeration. Asserts no errors flowed into the bag.
		public static void RunCodegen(Project p)
		{
			p.Plugins = PluginLoader.Discover(p.Root);
			p.Diagnostics = new DiagnosticBag();

			EntitiesExtension extension = new();
			extension.PreSourceDiscovery(p.SrcDir, p.Plugins, p.Diagnostics);
		}

		// Runs the full pipeline: codegen, compile, transpile, render.
		// Returns a dict of relative Luau paths to their content so
		// tests can assert on specific outputs.
		public static Dictionary<string, string> RunFullPipeline(Project p)
		{
			RunCodegen(p);

			Directory.CreateDirectory(p.OutDir);

			List<string> sources = Directory
				.EnumerateFiles(p.SrcDir, "*.cs", SearchOption.AllDirectories)
				.ToList();

			RojoResolver resolver = RojoResolver.FromPath(Path.Combine(p.Root, "default.project.json"));
			PathTranslator translator = new(p.SrcDir, p.OutDir);

			CSharpCompiler compiler = new();
			CompileResult result = compiler.CompileProject(sources, p.Plugins, projectRoot: p.SrcDir, rootNamespace: "EntitiesTest");

			LuaRenderer renderer = new();
			Dictionary<string, string> outputs = new(StringComparer.OrdinalIgnoreCase);

			foreach (CSharpCompilationContext ctx in result.Contexts)
			{
				TransformerState state = new(
					ctx, resolver, translator, p.Diagnostics, result.PluginBindings,
					Array.Empty<IRobloxCSharpExtension>());
				LuaNode unit = state.Transform(ctx.Root);
				if (unit is LuaCompilationUnit { SkipEmit: true }) continue;
				string lua = renderer.Render(unit);

				string srcPath = ctx.SyntaxTree.FilePath;
				string outPath = translator.GetOutputPath(srcPath);
				string rel = Path.GetRelativePath(p.OutDir, outPath).Replace('\\', '/');
				outputs[rel] = lua;
			}

			return outputs;
		}

		// Helpers — read a Generated C# file by name (e.g. "GameEntity.cs"),
		// or read a Luau file by name (e.g. "shared/Generated/GameEntity.luau").
		public static string ReadGenerated(Project p, string fileName)
		{
			string path = Path.Combine(p.GeneratedDir, fileName);
			Assert.True(File.Exists(path), $"Expected generated file {path}");
			return File.ReadAllText(path);
		}

		public static bool GeneratedExists(Project p, string fileName)
			=> File.Exists(Path.Combine(p.GeneratedDir, fileName));

		public static IEnumerable<string> ListGenerated(Project p)
		{
			if (!Directory.Exists(p.GeneratedDir)) return Array.Empty<string>();
			return Directory.EnumerateFiles(p.GeneratedDir, "*.cs")
				.Select(Path.GetFileName)
				.OrderBy(x => x, StringComparer.Ordinal);
		}

		// Trims and collapses whitespace runs so tests assert on logical
		// content rather than exact formatting. Use for substring matches
		// where indentation noise would otherwise dominate.
		public static string Squish(string s)
		{
			return System.Text.RegularExpressions.Regex
				.Replace(s, @"\s+", " ")
				.Trim();
		}

		private static void CopyDirectory(string src, string dst)
		{
			Directory.CreateDirectory(dst);
			foreach (string file in Directory.EnumerateFiles(src, "*", SearchOption.AllDirectories))
			{
				string rel = Path.GetRelativePath(src, file);
				string target = Path.Combine(dst, rel);
				Directory.CreateDirectory(Path.GetDirectoryName(target)!);
				File.Copy(file, target, overwrite: true);
			}
		}
	}
}
