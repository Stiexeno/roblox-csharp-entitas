using RobloxCSharp.Common.Diagnostics;

namespace Entities.Tests
{
	// Codegen-time guards: shapes that would compile fine and then fail
	// at runtime (or silently generate nothing) must surface in the
	// DiagnosticBag during PreSourceDiscovery.
	public class ComponentValidationTests
	{
		private static TestHarness.Project Run(string testName, string source)
		{
			TestHarness.Project p = TestHarness.Setup(testName);
			TestHarness.WriteSource(p, "Game.cs", source);
			TestHarness.RunCodegen(p);
			return p;
		}

		[Fact]
		public void Replicated_WithSevenFields_ReportsError()
		{
			TestHarness.Project p = Run(nameof(Replicated_WithSevenFields_ReportsError), @"
using Entities;
using Entities.CodeGeneration.Attributes;
namespace G
{
	[Game] [Replicated]
	public class Stats : IComponent
	{
		public int A; public int B; public int C; public int D;
		public int E; public int F; public int G;
	}
}");

			Assert.True(p.Diagnostics.HasErrors);
			Assert.Contains(p.Diagnostics.Items, d => d.Code == "ENT0001" && d.Message.Contains("Stats"));
		}

		[Fact]
		public void Replicated_WithSixFields_IsClean()
		{
			TestHarness.Project p = Run(nameof(Replicated_WithSixFields_IsClean), @"
using Entities;
using Entities.CodeGeneration.Attributes;
namespace G
{
	[Game] [Replicated]
	public class Stats : IComponent
	{
		public int A; public int B; public int C; public int D;
		public int E; public int F;
	}
}");

			Assert.False(p.Diagnostics.HasErrors);
		}

		[Fact]
		public void Component_WithPublicProperty_ReportsWarning()
		{
			TestHarness.Project p = Run(nameof(Component_WithPublicProperty_ReportsWarning), @"
using Entities;
using Entities.CodeGeneration.Attributes;
namespace G
{
	[Game]
	public class Health : IComponent
	{
		public int Value { get; set; }
	}
}");

			Assert.Contains(p.Diagnostics.Items, d => d.Code == "ENT0002" && d.Message.Contains("Health"));
			Assert.False(p.Diagnostics.HasErrors);
		}

		[Fact]
		public void AbstractComponent_WithContextAttribute_ReportsWarning()
		{
			TestHarness.Project p = Run(nameof(AbstractComponent_WithContextAttribute_ReportsWarning), @"
using Entities;
using Entities.CodeGeneration.Attributes;
namespace G
{
	[Game]
	public abstract class BaseStat : IComponent
	{
		public int Value;
	}
}");

			Assert.Contains(p.Diagnostics.Items, d => d.Code == "ENT0003" && d.Message.Contains("BaseStat"));
			Assert.False(p.Diagnostics.HasErrors);
		}

		[Fact]
		public void Component_NamedCommand_ReportsReservedNameError()
		{
			TestHarness.Project p = Run(nameof(Component_NamedCommand_ReportsReservedNameError), @"
using Entities;
using Entities.CodeGeneration.Attributes;
namespace G
{
	[Game]
	public class Command : IComponent
	{
		public int Value;
	}
}");

			Assert.True(p.Diagnostics.HasErrors);
			Assert.Contains(p.Diagnostics.Items, d => d.Code == "ENT0004");
		}

		[Fact]
		public void Component_WithFieldsOnly_IsClean()
		{
			TestHarness.Project p = Run(nameof(Component_WithFieldsOnly_IsClean), @"
using Entities;
using Entities.CodeGeneration.Attributes;
namespace G
{
	[Game]
	public class Health : IComponent
	{
		public int Value;
	}
}");

			Assert.Empty(p.Diagnostics.Items);
		}
	}
}
