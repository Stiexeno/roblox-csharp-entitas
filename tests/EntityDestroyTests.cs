namespace Entities.Tests
{
	// Structural assertions over runtime/Entity.luau's destroy path. No
	// Luau VM here, but the Entitas-parity invariants are source-visible:
	// every component removal during destroy must flow through the same
	// context-notification path as RemoveComponent, or cached groups keep
	// (and later resurrect, via the entity pool) destroyed entities.
	public class EntityDestroyTests
	{
		private static string ReadRuntime(string fileName)
		{
			string path = Path.Combine(TestHarness.PluginRoot, "runtime", fileName);
			Assert.True(File.Exists(path), $"Expected runtime file {path}");
			return File.ReadAllText(path);
		}

		private static string ExtractFunction(string src, string header)
		{
			int start = src.IndexOf(header, StringComparison.Ordinal);
			Assert.True(start >= 0, $"Expected `{header}` in runtime source");
			int end = src.IndexOf("\nfunction ", start, StringComparison.Ordinal);
			return end < 0 ? src[start..] : src[start..end];
		}

		[Fact]
		public void RemoveAllComponents_NotifiesContextPerComponent()
		{
			string src = ReadRuntime("Entity.luau");
			string body = ExtractFunction(src, "function Entity:RemoveAllComponents()");

			Assert.Contains("_NotifyContext(index, nil)", body);
			Assert.Contains("_PushToPool(index, component)", body);
		}

		[Fact]
		public void Destroy_DisablesBeforeStrippingComponents()
		{
			string src = ReadRuntime("Entity.luau");
			string body = ExtractFunction(src, "function Entity:Destroy()");

			int disable = body.IndexOf("self._isEnabled = false", StringComparison.Ordinal);
			int strip = body.IndexOf("self:RemoveAllComponents()", StringComparison.Ordinal);
			Assert.True(disable >= 0 && strip > disable,
				"Destroy must disable the entity before stripping components (Entitas InternalDestroy order)");
		}

		[Fact]
		public void Destroy_ErrorsOnDoubleDestroy()
		{
			string src = ReadRuntime("Entity.luau");
			string body = ExtractFunction(src, "function Entity:Destroy()");

			Assert.Contains("if not self._isEnabled then", body);
			Assert.Contains("error(", body);
		}
	}
}
