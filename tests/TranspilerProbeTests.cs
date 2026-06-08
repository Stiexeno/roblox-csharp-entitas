namespace Entities.Tests
{
	// Probes for transpiler-level C# features (not entities-specific).
	// Each test runs a tiny source through the compile + transpile + render
	// pipeline and asserts on the resulting Lua so the .claude quirks doc
	// can stay honest about what the converter actually supports.
	public class TranspilerProbeTests
	{
		private static string TranspileAndGetMain(string testName, string source)
		{
			TestHarness.Project p = TestHarness.Setup(testName);
			TestHarness.WriteSource(p, "Probe.cs", source);
			System.Collections.Generic.Dictionary<string, string> outputs = TestHarness.RunFullPipeline(p);
			foreach (System.Collections.Generic.KeyValuePair<string, string> kvp in outputs)
			{
				if (kvp.Key.EndsWith("Probe.luau", System.StringComparison.OrdinalIgnoreCase))
					return kvp.Value;
			}
			throw new System.InvalidOperationException(
				"No Probe.luau in outputs. Got: " + string.Join(", ", outputs.Keys));
		}

		[Fact]
		public void OutVarImplicit_AtCallSite_EmitsNotImplemented()
		{
			// `if (d.TryGetValue(key, out var v)) return v;`
			// Pins the current gap: DeclarationExpressionSyntax has no
			// transformer, so the arg slot becomes a NotImplemented comment
			// and any subsequent reference to `v` is a phantom local.
			// If this test starts failing because the marker is gone, the
			// converter has gained support — flip the assertion to require
			// a `local v` binding and update .claude/rules/roblox-csharp-quirks.md.
			string lua = TranspileAndGetMain(
				nameof(OutVarImplicit_AtCallSite_EmitsNotImplemented),
				@"using System.Collections.Generic;
namespace U {
	public class P {
		public int Get(Dictionary<int, int> d, int key) {
			if (d.TryGetValue(key, out var v)) return v;
			return 0;
		}
	}
}");
			Assert.Contains("NotImplemented: DeclarationExpressionSyntax", lua);
		}

		[Fact]
		public void OutVarExplicit_AtCallSite_EmitsNotImplemented()
		{
			// `if (d.TryGetValue(key, out int v)) return v;` — same gap.
			string lua = TranspileAndGetMain(
				nameof(OutVarExplicit_AtCallSite_EmitsNotImplemented),
				@"using System.Collections.Generic;
namespace U {
	public class P {
		public int Get(Dictionary<int, int> d, int key) {
			if (d.TryGetValue(key, out int v)) return v;
			return 0;
		}
	}
}");
			Assert.Contains("NotImplemented: DeclarationExpressionSyntax", lua);
		}

		[Fact]
		public void IsPattern_DeclaresLocal()
		{
			// `if (obj is int i) return i;` — known supported via
			// IsPatternExpressionTransformer; this just locks it down.
			string lua = TranspileAndGetMain(
				nameof(IsPattern_DeclaresLocal),
				@"namespace U {
	public class P {
		public int Get(object obj) {
			if (obj is int i) return i;
			return 0;
		}
	}
}");
			Assert.True(lua.Contains("local i") || lua.Contains("i "),
				"`is int i` didn't surface an `i` binding in the Lua. Actual:\n" + lua);
		}

		[Fact]
		public void AsyncAwait_LowersToInnerCall()
		{
			// `await GetNumber()` — AwaitExpressionTransformer unwraps to
			// the inner call. The renderer's "coroutine handling for async"
			// TODO means there's no actual concurrency, but the syntax
			// flows through synchronously.
			string lua = TranspileAndGetMain(
				nameof(AsyncAwait_LowersToInnerCall),
				@"using System.Threading.Tasks;
namespace U {
	public class P {
		public async Task<int> Run() {
			int x = await GetNumber();
			return x;
		}
		private async Task<int> GetNumber() { return 42; }
	}
}");
			Assert.Contains("GetNumber", lua);
		}

		[Fact]
		public void TupleDeconstruction_DeclaresLocals()
		{
			// `(int a, int b) = Method();` — tuple deconstruction in a
			// declaration. Pins the current behavior.
			string lua = TranspileAndGetMain(
				nameof(TupleDeconstruction_DeclaresLocals),
				@"namespace U {
	public class P {
		public int Get() {
			(int a, int b) = Pair();
			return a + b;
		}
		private (int, int) Pair() { return (1, 2); }
	}
}");
			Assert.True(lua.Contains("local a") || lua.Contains("a, b") || lua.Contains("a "),
				"tuple decon didn't surface an `a` binding. Actual:\n" + lua);
		}
	}
}
