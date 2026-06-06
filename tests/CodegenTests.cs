namespace Entitas.Tests
{
	// Tests for EntitasExtension.PreSourceDiscovery — assert on the
	// generated C# (src/Generated/*.cs) content shape, file presence,
	// and the discovery rules (which IComponent classes get picked up,
	// which contexts they route to).
	public class CodegenTests
	{
		private const string OneGamePlayer = @"
using Entitas;
using Entitas.CodeGeneration.Attributes;
namespace G { [Game] public class Player : IComponent { } }
";

		private const string OneGameHealth = @"
using Entitas;
using Entitas.CodeGeneration.Attributes;
namespace G { [Game] public class Health : IComponent { public int Value; } }
";

		private static TestHarness.Project Run(string testName, string source)
		{
			TestHarness.Project p = TestHarness.Setup(testName);
			TestHarness.WriteSource(p, "Game.cs", source);
			TestHarness.RunCodegen(p);
			return p;
		}

		// ----------------------------------------------------------------
		// File presence: which Generated/*.cs files appear after codegen.
		// ----------------------------------------------------------------

		[Fact]
		public void Codegen_EmitsAllExpectedFiles_ForSingleContext()
		{
			TestHarness.Project p = Run(nameof(Codegen_EmitsAllExpectedFiles_ForSingleContext), OneGamePlayer);

			Assert.True(TestHarness.GeneratedExists(p, "Contexts.cs"));
			Assert.True(TestHarness.GeneratedExists(p, "GameContext.cs"));
			Assert.True(TestHarness.GeneratedExists(p, "GameEntity.cs"));
			Assert.True(TestHarness.GeneratedExists(p, "GameMatcher.cs"));
			Assert.True(TestHarness.GeneratedExists(p, "GameComponentsLookup.cs"));
		}

		[Fact]
		public void Codegen_EmitsPerContextFiles_ForMultipleContexts()
		{
			string source = @"
using Entitas;
using Entitas.CodeGeneration.Attributes;
namespace Ents
{
	public class InputAttribute : ContextAttribute { public InputAttribute() : base(""Input"") { } }
	[Game] public class Health : IComponent { public int Value; }
	[Input] public class MouseDown : IComponent { }
}";

			TestHarness.Project p = Run(nameof(Codegen_EmitsPerContextFiles_ForMultipleContexts), source);

			Assert.True(TestHarness.GeneratedExists(p, "GameContext.cs"));
			Assert.True(TestHarness.GeneratedExists(p, "GameEntity.cs"));
			Assert.True(TestHarness.GeneratedExists(p, "GameMatcher.cs"));
			Assert.True(TestHarness.GeneratedExists(p, "GameComponentsLookup.cs"));
			Assert.True(TestHarness.GeneratedExists(p, "InputContext.cs"));
			Assert.True(TestHarness.GeneratedExists(p, "InputEntity.cs"));
			Assert.True(TestHarness.GeneratedExists(p, "InputMatcher.cs"));
			Assert.True(TestHarness.GeneratedExists(p, "InputComponentsLookup.cs"));
			Assert.True(TestHarness.GeneratedExists(p, "Contexts.cs"));
		}

		[Fact]
		public void Codegen_EmitsNothing_WhenSourceHasNoComponents()
		{
			TestHarness.Project p = Run(nameof(Codegen_EmitsNothing_WhenSourceHasNoComponents),
				@"namespace G { public class Plain { public int X; } }");

			Assert.Empty(TestHarness.ListGenerated(p));
		}

		[Fact]
		public void Codegen_IgnoresAbstractComponents()
		{
			string source = @"
using Entitas;
using Entitas.CodeGeneration.Attributes;
namespace G {
	[Game] public abstract class Base : IComponent { }
	[Game] public class Real : IComponent { }
}";
			TestHarness.Project p = Run(nameof(Codegen_IgnoresAbstractComponents), source);

			string lookup = TestHarness.ReadGenerated(p, "GameComponentsLookup.cs");
			Assert.Contains("public const int Real = 0;", lookup);
			Assert.DoesNotContain("public const int Base", lookup);
		}

		[Fact]
		public void Codegen_IgnoresUntaggedIComponentImplementers()
		{
			string source = @"
using Entitas;
namespace G {
	public class NoTag : IComponent { }   // No [Game] attribute — should be skipped.
}";
			TestHarness.Project p = Run(nameof(Codegen_IgnoresUntaggedIComponentImplementers), source);
			Assert.Empty(TestHarness.ListGenerated(p));
		}

		// ----------------------------------------------------------------
		// ComponentsLookup shape.
		// ----------------------------------------------------------------

		[Fact]
		public void ComponentsLookup_AssignsZeroBasedIndicesInSortedOrder()
		{
			string source = @"
using Entitas;
using Entitas.CodeGeneration.Attributes;
namespace G {
	[Game] public class Zebra : IComponent { }
	[Game] public class Apple : IComponent { }
	[Game] public class Mango : IComponent { }
}";
			TestHarness.Project p = Run(nameof(ComponentsLookup_AssignsZeroBasedIndicesInSortedOrder), source);

			string lookup = TestHarness.ReadGenerated(p, "GameComponentsLookup.cs");
			Assert.Contains("public const int Apple = 0;", lookup);
			Assert.Contains("public const int Mango = 1;", lookup);
			Assert.Contains("public const int Zebra = 2;", lookup);
		}

		[Fact]
		public void ComponentsLookup_TotalComponents_MatchesCount()
		{
			string source = @"
using Entitas;
using Entitas.CodeGeneration.Attributes;
namespace G {
	[Game] public class A : IComponent { }
	[Game] public class B : IComponent { }
	[Game] public class C : IComponent { }
}";
			TestHarness.Project p = Run(nameof(ComponentsLookup_TotalComponents_MatchesCount), source);
			string lookup = TestHarness.ReadGenerated(p, "GameComponentsLookup.cs");
			Assert.Contains("public const int TotalComponents = 3;", lookup);
		}

		[Fact]
		public void ComponentsLookup_HasComponentNamesArray()
		{
			TestHarness.Project p = Run(nameof(ComponentsLookup_HasComponentNamesArray), OneGamePlayer);
			string lookup = TestHarness.ReadGenerated(p, "GameComponentsLookup.cs");
			Assert.Contains("componentNames", lookup);
			Assert.Contains("\"Player\"", lookup);
		}

		[Fact]
		public void ComponentsLookup_HasComponentTypesArray()
		{
			TestHarness.Project p = Run(nameof(ComponentsLookup_HasComponentTypesArray), OneGamePlayer);
			string lookup = TestHarness.ReadGenerated(p, "GameComponentsLookup.cs");
			Assert.Contains("componentTypes", lookup);
			Assert.Contains("typeof(global::G.Player)", lookup);
		}

		// ----------------------------------------------------------------
		// GameEntity shape — flag vs single-Value vs multi-field.
		// ----------------------------------------------------------------

		[Fact]
		public void EntityFlag_EmitsIsXPropertyWithGetterAndSetter()
		{
			TestHarness.Project p = Run(nameof(EntityFlag_EmitsIsXPropertyWithGetterAndSetter), OneGamePlayer);
			string entity = TestHarness.ReadGenerated(p, "GameEntity.cs");
			Assert.Contains("public bool IsPlayer", entity);
			Assert.Contains("get { return HasComponent(GameComponentsLookup.Player); }", entity);
			Assert.Contains("AddComponent(GameComponentsLookup.Player, _PlayerComponent)", entity);
			Assert.Contains("RemoveComponent(GameComponentsLookup.Player)", entity);
		}

		[Fact]
		public void EntityFlag_HasStaticSingletonForReuse()
		{
			TestHarness.Project p = Run(nameof(EntityFlag_HasStaticSingletonForReuse), OneGamePlayer);
			string entity = TestHarness.ReadGenerated(p, "GameEntity.cs");
			Assert.Contains("static readonly global::G.Player _PlayerComponent = new();", entity);
		}

		[Fact]
		public void EntityValue_SingleValueField_EmitsUnwrappingGetterAndSetter()
		{
			TestHarness.Project p = Run(nameof(EntityValue_SingleValueField_EmitsUnwrappingGetterAndSetter), OneGameHealth);
			string entity = TestHarness.ReadGenerated(p, "GameEntity.cs");

			Assert.Contains("public int Health", entity);
			Assert.Contains(").Value;", entity);
			Assert.Contains("ReplaceComponent(GameComponentsLookup.Health, component);", entity);
		}

		[Fact]
		public void EntityValue_AlwaysEmitsHasXProperty()
		{
			TestHarness.Project p = Run(nameof(EntityValue_AlwaysEmitsHasXProperty), OneGameHealth);
			string entity = TestHarness.ReadGenerated(p, "GameEntity.cs");
			Assert.Contains("public bool HasHealth", entity);
			Assert.Contains("HasComponent(GameComponentsLookup.Health)", entity);
		}

		[Fact]
		public void EntityValue_AlwaysEmitsAddReplaceRemoveMethods()
		{
			TestHarness.Project p = Run(nameof(EntityValue_AlwaysEmitsAddReplaceRemoveMethods), OneGameHealth);
			string entity = TestHarness.ReadGenerated(p, "GameEntity.cs");
			Assert.Contains("AddHealth(int newValue)", entity);
			Assert.Contains("ReplaceHealth(int newValue)", entity);
			Assert.Contains("public void RemoveHealth()", entity);
		}

		[Fact]
		public void EntityValue_MultiField_DoesNotUnwrap()
		{
			string source = @"
using Entitas;
using Entitas.CodeGeneration.Attributes;
namespace G { [Game] public class Pos : IComponent { public int X; public int Y; } }";
			TestHarness.Project p = Run(nameof(EntityValue_MultiField_DoesNotUnwrap), source);
			string entity = TestHarness.ReadGenerated(p, "GameEntity.cs");

			// Multi-field — the property returns the component instance,
			// not an unwrapped value. Setter is intentionally absent (the
			// user must call ReplacePos(x, y)).
			Assert.Contains("public global::G.Pos Pos", entity);
			Assert.Contains("(global::G.Pos)GetComponent(GameComponentsLookup.Pos)", entity);
			Assert.DoesNotContain("public global::G.Pos Pos\r\n\t{\r\n\t\tget", entity); // not a get/set block
			Assert.Contains("AddPos(int newX, int newY)", entity);
			Assert.Contains("component.X = newX;", entity);
			Assert.Contains("component.Y = newY;", entity);
		}

		[Fact]
		public void EntityValue_SingleNonValueField_DoesNotUnwrap()
		{
			string source = @"
using Entitas;
using Entitas.CodeGeneration.Attributes;
namespace G { [Game] public class AttackEvent : IComponent { public int AttackerId; } }";
			TestHarness.Project p = Run(nameof(EntityValue_SingleNonValueField_DoesNotUnwrap), source);
			string entity = TestHarness.ReadGenerated(p, "GameEntity.cs");

			// Field is named AttackerId, not Value — unwrap rule must not
			// fire. Property returns the component instance.
			Assert.Contains("public global::G.AttackEvent AttackEvent", entity);
			Assert.Contains("AddAttackEvent(int newAttackerId)", entity);
		}

		[Fact]
		public void EntityValue_PreservesFullyQualifiedFieldType()
		{
			// Verify that field type names get fully-qualified (so the
			// codegen output compiles even when the user hasn't pulled
			// in the right `using`).
			string source = @"
using Entitas;
using Entitas.CodeGeneration.Attributes;
namespace Custom { public struct Vec3 { } }
namespace G {
	[Game] public class Position : IComponent { public Custom.Vec3 Value; }
}";
			TestHarness.Project p = Run(nameof(EntityValue_PreservesFullyQualifiedFieldType), source);
			string entity = TestHarness.ReadGenerated(p, "GameEntity.cs");
			Assert.Contains("public global::Custom.Vec3 Position", entity);
		}

		[Fact]
		public void EntityFlag_DoesNotEmitAddXMethod()
		{
			// Flag components have no fields to assign — there's no
			// matching AddPlayer / ReplacePlayer signature, just the
			// IsPlayer property + the singleton.
			TestHarness.Project p = Run(nameof(EntityFlag_DoesNotEmitAddXMethod), OneGamePlayer);
			string entity = TestHarness.ReadGenerated(p, "GameEntity.cs");
			Assert.DoesNotContain("AddPlayer(", entity);
			Assert.DoesNotContain("ReplacePlayer(", entity);
		}

		[Fact]
		public void Entity_ExtendsEntitasEntity()
		{
			TestHarness.Project p = Run(nameof(Entity_ExtendsEntitasEntity), OneGamePlayer);
			string entity = TestHarness.ReadGenerated(p, "GameEntity.cs");
			Assert.Contains("public sealed partial class GameEntity : Entity", entity);
		}

		// ----------------------------------------------------------------
		// GameMatcher shape.
		// ----------------------------------------------------------------

		[Fact]
		public void Matcher_EmitsPerComponentStaticGetter()
		{
			TestHarness.Project p = Run(nameof(Matcher_EmitsPerComponentStaticGetter), OneGamePlayer);
			string matcher = TestHarness.ReadGenerated(p, "GameMatcher.cs");
			Assert.Contains("public static IMatcher<GameEntity> Player", matcher);
			Assert.Contains("Matcher.AllOfIndices<GameEntity>(GameComponentsLookup.Player)", matcher);
		}

		[Fact]
		public void Matcher_CachesPerComponentInstance()
		{
			TestHarness.Project p = Run(nameof(Matcher_CachesPerComponentInstance), OneGamePlayer);
			string matcher = TestHarness.ReadGenerated(p, "GameMatcher.cs");
			Assert.Contains("static IMatcher<GameEntity> _matcherPlayer;", matcher);
			Assert.Contains("if (_matcherPlayer == null)", matcher);
		}

		[Fact]
		public void Matcher_EmitsAllOfAndAnyOfFactories()
		{
			TestHarness.Project p = Run(nameof(Matcher_EmitsAllOfAndAnyOfFactories), OneGamePlayer);
			string matcher = TestHarness.ReadGenerated(p, "GameMatcher.cs");
			Assert.Contains("AllOf(params IMatcher<GameEntity>[] matchers)", matcher);
			Assert.Contains("AnyOf(params IMatcher<GameEntity>[] matchers)", matcher);
		}

		[Fact]
		public void Matcher_AssignsComponentNamesForDebug()
		{
			// Jenny's matcher carries componentNames so its ToString /
			// debug shows the human-readable name. Replicate verbatim.
			TestHarness.Project p = Run(nameof(Matcher_AssignsComponentNamesForDebug), OneGamePlayer);
			string matcher = TestHarness.ReadGenerated(p, "GameMatcher.cs");
			Assert.Contains("m.componentNames = GameComponentsLookup.componentNames", matcher);
		}

		// ----------------------------------------------------------------
		// GameContext shape.
		// ----------------------------------------------------------------

		[Fact]
		public void Context_ExtendsContextOfTEntity()
		{
			TestHarness.Project p = Run(nameof(Context_ExtendsContextOfTEntity), OneGamePlayer);
			string context = TestHarness.ReadGenerated(p, "GameContext.cs");
			Assert.Contains("public sealed partial class GameContext : Context<GameEntity>", context);
		}

		[Fact]
		public void Context_PassesTotalComponentsToBase()
		{
			TestHarness.Project p = Run(nameof(Context_PassesTotalComponentsToBase), OneGamePlayer);
			string context = TestHarness.ReadGenerated(p, "GameContext.cs");
			Assert.Contains(": base(GameComponentsLookup.TotalComponents)", context);
		}

		[Fact]
		public void Context_OverridesCreateEntityInstance()
		{
			// The runtime's Context.CreateEntity calls CreateEntityInstance
			// via virtual dispatch — the partial must override it to
			// return the concrete typed entity. (No generic-`new T()` in Lua.)
			TestHarness.Project p = Run(nameof(Context_OverridesCreateEntityInstance), OneGamePlayer);
			string context = TestHarness.ReadGenerated(p, "GameContext.cs");
			Assert.Contains("public override GameEntity CreateEntityInstance()", context);
			Assert.Contains("return new GameEntity();", context);
		}

		// ----------------------------------------------------------------
		// Contexts (the global aggregate).
		// ----------------------------------------------------------------

		[Fact]
		public void Contexts_ImplementsIContexts()
		{
			TestHarness.Project p = Run(nameof(Contexts_ImplementsIContexts), OneGamePlayer);
			string contexts = TestHarness.ReadGenerated(p, "Contexts.cs");
			Assert.Contains("public partial class Contexts : IContexts", contexts);
		}

		[Fact]
		public void Contexts_ExposesSharedInstance()
		{
			TestHarness.Project p = Run(nameof(Contexts_ExposesSharedInstance), OneGamePlayer);
			string contexts = TestHarness.ReadGenerated(p, "Contexts.cs");
			Assert.Contains("public static Contexts sharedInstance", contexts);
			Assert.Contains("_sharedInstance = new Contexts()", contexts);
		}

		[Fact]
		public void Contexts_HasOneLowerCasePropertyPerContext()
		{
			string source = @"
using Entitas;
using Entitas.CodeGeneration.Attributes;
namespace Ents {
	public class InputAttribute : ContextAttribute { public InputAttribute() : base(""Input"") { } }
	[Game] public class A : IComponent { }
	[Input] public class B : IComponent { }
}";
			TestHarness.Project p = Run(nameof(Contexts_HasOneLowerCasePropertyPerContext), source);
			string contexts = TestHarness.ReadGenerated(p, "Contexts.cs");
			Assert.Contains("public GameContext game", contexts);
			Assert.Contains("public InputContext input", contexts);
		}

		[Fact]
		public void Contexts_AllContextsArray_IncludesEveryContext()
		{
			string source = @"
using Entitas;
using Entitas.CodeGeneration.Attributes;
namespace Ents {
	public class InputAttribute : ContextAttribute { public InputAttribute() : base(""Input"") { } }
	[Game] public class A : IComponent { }
	[Input] public class B : IComponent { }
}";
			TestHarness.Project p = Run(nameof(Contexts_AllContextsArray_IncludesEveryContext), source);
			string contexts = TestHarness.ReadGenerated(p, "Contexts.cs");
			Assert.Contains("public IContext[] allContexts", contexts);
			Assert.Contains("new IContext[] { game, input }", contexts);
		}

		[Fact]
		public void Contexts_ResetWalksEveryContext()
		{
			TestHarness.Project p = Run(nameof(Contexts_ResetWalksEveryContext), OneGamePlayer);
			string contexts = TestHarness.ReadGenerated(p, "Contexts.cs");
			Assert.Contains("public void Reset()", contexts);
			Assert.Contains("all[i].Reset()", contexts);
		}

		// ----------------------------------------------------------------
		// Multi-context routing — same component cannot be in two contexts
		// today, but a flag in one and a value in another should not
		// collide.
		// ----------------------------------------------------------------

		[Fact]
		public void Codegen_DoesNotMixComponentsBetweenContexts()
		{
			string source = @"
using Entitas;
using Entitas.CodeGeneration.Attributes;
namespace Ents {
	public class InputAttribute : ContextAttribute { public InputAttribute() : base(""Input"") { } }
	[Game] public class GameOnly : IComponent { }
	[Input] public class InputOnly : IComponent { }
}";
			TestHarness.Project p = Run(nameof(Codegen_DoesNotMixComponentsBetweenContexts), source);

			string gameLookup = TestHarness.ReadGenerated(p, "GameComponentsLookup.cs");
			string inputLookup = TestHarness.ReadGenerated(p, "InputComponentsLookup.cs");

			Assert.Contains("GameOnly", gameLookup);
			Assert.DoesNotContain("InputOnly", gameLookup);
			Assert.Contains("InputOnly", inputLookup);
			Assert.DoesNotContain("GameOnly", inputLookup);
		}

		// ----------------------------------------------------------------
		// Component pool — codegen-emitted Add/Replace/setter bodies must
		// route through CreateComponent<T>(index) so removed/replaced
		// instances recycle through Context._componentPools.
		// ----------------------------------------------------------------

		[Fact]
		public void EntityValue_AddX_BodyRoutesThroughCreateComponent()
		{
			TestHarness.Project p = Run(nameof(EntityValue_AddX_BodyRoutesThroughCreateComponent), OneGameHealth);
			string entity = TestHarness.ReadGenerated(p, "GameEntity.cs");
			Assert.Contains("CreateComponent<global::G.Health>(GameComponentsLookup.Health)", entity);
			Assert.DoesNotContain("global::G.Health component = new();", entity);
		}

		[Fact]
		public void EntityValue_ReplaceX_BodyRoutesThroughCreateComponent()
		{
			TestHarness.Project p = Run(nameof(EntityValue_ReplaceX_BodyRoutesThroughCreateComponent), OneGameHealth);
			string entity = TestHarness.ReadGenerated(p, "GameEntity.cs");
			// Both Add and Replace lines should match; count to verify both.
			int hits = System.Text.RegularExpressions.Regex.Matches(
				entity, @"CreateComponent<global::G\.Health>\(GameComponentsLookup\.Health\)").Count;
			Assert.True(hits >= 2, $"Expected CreateComponent in both Add and Replace bodies, found {hits}.");
		}

		[Fact]
		public void EntityValue_SetterRoutesThroughCreateComponent()
		{
			// `entity.Health = 20` setter goes through CreateComponent so
			// pool recycling applies even when users prefer the property
			// syntax over ReplaceHealth(...).
			TestHarness.Project p = Run(nameof(EntityValue_SetterRoutesThroughCreateComponent), OneGameHealth);
			string entity = TestHarness.ReadGenerated(p, "GameEntity.cs");
			// Find the setter block and verify it calls CreateComponent.
			Assert.Contains("set", entity);
			Assert.Contains("CreateComponent<global::G.Health>", entity);
		}
	}
}
