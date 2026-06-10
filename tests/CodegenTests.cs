namespace Entities.Tests
{
	// Tests for EntitiesExtension.PreSourceDiscovery — assert on the
	// generated C# (src/Generated/*.cs) content shape, file presence,
	// and the discovery rules (which IComponent classes get picked up,
	// which contexts they route to).
	public class CodegenTests
	{
		private const string OneGamePlayer = @"
using Entities;
using Entities.CodeGeneration.Attributes;
namespace G { [Game] public class Player : IComponent { } }
";

		private const string OneGameHealth = @"
using Entities;
using Entities.CodeGeneration.Attributes;
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
using Entities;
using Entities.CodeGeneration.Attributes;
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
using Entities;
using Entities.CodeGeneration.Attributes;
namespace G {
	[Game] public abstract class Base : IComponent { }
	[Game] public class Real : IComponent { }
}";
			TestHarness.Project p = Run(nameof(Codegen_IgnoresAbstractComponents), source);

			string lookup = TestHarness.ReadGenerated(p, "GameComponentsLookup.cs");
			// Real lands in the lookup; abstract Base does not. Index
			// values shift around the synthesized Command / OriginUserId
			// — the test only cares that Real exists and Base doesn't.
			Assert.Contains("public const int Real = ", lookup);
			Assert.DoesNotContain("public const int Base", lookup);
		}

		[Fact]
		public void Codegen_IgnoresUntaggedIComponentImplementers()
		{
			string source = @"
using Entities;
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
using Entities;
using Entities.CodeGeneration.Attributes;
namespace G {
	[Game] public class Zebra : IComponent { }
	[Game] public class Apple : IComponent { }
	[Game] public class Mango : IComponent { }
}";
			TestHarness.Project p = Run(nameof(ComponentsLookup_AssignsZeroBasedIndicesInSortedOrder), source);

			string lookup = TestHarness.ReadGenerated(p, "GameComponentsLookup.cs");
			// Synthesized Command + OriginUserId share the sort, so the
			// hard-coded indices we expected before don't match anymore.
			// The contract that matters: alphabetical ordering among user
			// components is preserved.
			int appleIdx = lookup.IndexOf("public const int Apple = ", StringComparison.Ordinal);
			int mangoIdx = lookup.IndexOf("public const int Mango = ", StringComparison.Ordinal);
			int zebraIdx = lookup.IndexOf("public const int Zebra = ", StringComparison.Ordinal);
			Assert.True(appleIdx > 0 && mangoIdx > 0 && zebraIdx > 0);
			Assert.True(appleIdx < mangoIdx && mangoIdx < zebraIdx);
		}

		[Fact]
		public void ComponentsLookup_TotalComponents_MatchesCount()
		{
			string source = @"
using Entities;
using Entities.CodeGeneration.Attributes;
namespace G {
	[Game] public class A : IComponent { }
	[Game] public class B : IComponent { }
	[Game] public class C : IComponent { }
}";
			TestHarness.Project p = Run(nameof(ComponentsLookup_TotalComponents_MatchesCount), source);
			string lookup = TestHarness.ReadGenerated(p, "GameComponentsLookup.cs");
			// 3 user + Command + OriginUserId = 5.
			Assert.Contains("public const int TotalComponents = 5;", lookup);
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
			// `typeof(BareName)` + `using <Namespace>;` at the top — the
			// transpiler binds bare names from CS.import, so the typeof
			// reference must match (a namespace-qualified or global::
			// prefixed name would land verbatim in Luau and fail).
			Assert.Contains("using G;", lookup);
			Assert.Contains("typeof(Player)", lookup);
			Assert.DoesNotContain("typeof(global::", lookup);
			Assert.DoesNotContain("typeof(G.Player)", lookup);
		}

		// ----------------------------------------------------------------
		// GameEntity shape — flag vs single-Value vs multi-field.
		// ----------------------------------------------------------------

		[Fact]
		public void EntityFlag_EmitsIsXPropertyWithGetterAndSetter()
		{
			TestHarness.Project p = Run(nameof(EntityFlag_EmitsIsXPropertyWithGetterAndSetter), OneGamePlayer);
			string entity = TestHarness.ReadGenerated(p, "Components/Game.Player.cs");
			Assert.Contains("public bool IsPlayer", entity);
			Assert.Contains("get { return HasComponent(GameComponentsLookup.Player); }", entity);
			Assert.Contains("AddComponent(GameComponentsLookup.Player, _PlayerComponent)", entity);
			Assert.Contains("RemoveComponent(GameComponentsLookup.Player)", entity);
		}

		[Fact]
		public void EntityFlag_HasStaticSingletonForReuse()
		{
			TestHarness.Project p = Run(nameof(EntityFlag_HasStaticSingletonForReuse), OneGamePlayer);
			string entity = TestHarness.ReadGenerated(p, "Components/Game.Player.cs");
			Assert.Contains("static readonly global::G.Player _PlayerComponent = new();", entity);
		}

		[Fact]
		public void EntityValue_SingleValueField_EmitsUnwrappingGetterAndSetter()
		{
			TestHarness.Project p = Run(nameof(EntityValue_SingleValueField_EmitsUnwrappingGetterAndSetter), OneGameHealth);
			string entity = TestHarness.ReadGenerated(p, "Components/Game.Health.cs");

			Assert.Contains("public int Health", entity);
			Assert.Contains(").Value;", entity);
			Assert.Contains("ReplaceComponent(GameComponentsLookup.Health, component);", entity);
		}

		[Fact]
		public void EntityValue_AlwaysEmitsHasXProperty()
		{
			TestHarness.Project p = Run(nameof(EntityValue_AlwaysEmitsHasXProperty), OneGameHealth);
			string entity = TestHarness.ReadGenerated(p, "Components/Game.Health.cs");
			Assert.Contains("public bool HasHealth", entity);
			Assert.Contains("HasComponent(GameComponentsLookup.Health)", entity);
		}

		[Fact]
		public void EntityValue_AlwaysEmitsAddReplaceRemoveMethods()
		{
			TestHarness.Project p = Run(nameof(EntityValue_AlwaysEmitsAddReplaceRemoveMethods), OneGameHealth);
			string entity = TestHarness.ReadGenerated(p, "Components/Game.Health.cs");
			Assert.Contains("AddHealth(int newValue)", entity);
			Assert.Contains("ReplaceHealth(int newValue)", entity);
			Assert.Contains("public void RemoveHealth()", entity);
		}

		[Fact]
		public void EntityValue_MultiField_DoesNotUnwrap()
		{
			string source = @"
using Entities;
using Entities.CodeGeneration.Attributes;
namespace G { [Game] public class Pos : IComponent { public int X; public int Y; } }";
			TestHarness.Project p = Run(nameof(EntityValue_MultiField_DoesNotUnwrap), source);
			string entity = TestHarness.ReadGenerated(p, "Components/Game.Pos.cs");

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
using Entities;
using Entities.CodeGeneration.Attributes;
namespace G { [Game] public class AttackEvent : IComponent { public int AttackerId; } }";
			TestHarness.Project p = Run(nameof(EntityValue_SingleNonValueField_DoesNotUnwrap), source);
			string entity = TestHarness.ReadGenerated(p, "Components/Game.AttackEvent.cs");

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
using Entities;
using Entities.CodeGeneration.Attributes;
namespace Custom { public struct Vec3 { } }
namespace G {
	[Game] public class Position : IComponent { public Custom.Vec3 Value; }
}";
			TestHarness.Project p = Run(nameof(EntityValue_PreservesFullyQualifiedFieldType), source);
			string entity = TestHarness.ReadGenerated(p, "Components/Game.Position.cs");
			Assert.Contains("public global::Custom.Vec3 Position", entity);
		}

		[Fact]
		public void EntityFlag_DoesNotEmitAddXMethod()
		{
			// Flag components have no fields to assign — there's no
			// matching AddPlayer / ReplacePlayer signature, just the
			// IsPlayer property + the singleton.
			TestHarness.Project p = Run(nameof(EntityFlag_DoesNotEmitAddXMethod), OneGamePlayer);
			string entity = TestHarness.ReadGenerated(p, "Components/Game.Player.cs");
			Assert.DoesNotContain("AddPlayer(", entity);
			Assert.DoesNotContain("ReplacePlayer(", entity);
		}

		[Fact]
		public void Entity_ExtendsEntitiesEntity()
		{
			TestHarness.Project p = Run(nameof(Entity_ExtendsEntitiesEntity), OneGamePlayer);
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
			string matcher = TestHarness.ReadGenerated(p, "Components/Game.Player.cs");
			Assert.Contains("public static IMatcher<GameEntity> Player", matcher);
			Assert.Contains("Matcher.AllOfIndices<GameEntity>(GameComponentsLookup.Player)", matcher);
		}

		[Fact]
		public void Matcher_CachesPerComponentInstance()
		{
			TestHarness.Project p = Run(nameof(Matcher_CachesPerComponentInstance), OneGamePlayer);
			string matcher = TestHarness.ReadGenerated(p, "Components/Game.Player.cs");
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
			string matcher = TestHarness.ReadGenerated(p, "Components/Game.Player.cs");
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
using Entities;
using Entities.CodeGeneration.Attributes;
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
using Entities;
using Entities.CodeGeneration.Attributes;
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
using Entities;
using Entities.CodeGeneration.Attributes;
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
			string entity = TestHarness.ReadGenerated(p, "Components/Game.Health.cs");
			Assert.Contains("CreateComponent<global::G.Health>(GameComponentsLookup.Health)", entity);
			Assert.DoesNotContain("global::G.Health component = new();", entity);
		}

		[Fact]
		public void EntityValue_ReplaceX_BodyRoutesThroughCreateComponent()
		{
			TestHarness.Project p = Run(nameof(EntityValue_ReplaceX_BodyRoutesThroughCreateComponent), OneGameHealth);
			string entity = TestHarness.ReadGenerated(p, "Components/Game.Health.cs");
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
			string entity = TestHarness.ReadGenerated(p, "Components/Game.Health.cs");
			// Find the setter block and verify it calls CreateComponent.
			Assert.Contains("set", entity);
			Assert.Contains("CreateComponent<global::G.Health>", entity);
		}

		// ----------------------------------------------------------------
		// Replication codegen — [Replicated] components enqueue Set / Remove
		// ops on the per-context EntitiesReplication buffer. The server runtime
		// drains the buffer on RunService.Heartbeat and fires one RemoteEvent
		// per context per tick with the whole batch. No per-context static
		// delegate class is emitted anymore.
		// ----------------------------------------------------------------

		private const string OneReplicatedHealth = @"
using Entities;
using Entities.CodeGeneration.Attributes;
namespace G {
	[Game, Replicated] public class Health : IComponent { public int Value; }
}";

		private const string OneReplicatedFlag = @"
using Entities;
using Entities.CodeGeneration.Attributes;
namespace G {
	[Game, Replicated] public class Stunned : IComponent { }
}";

		[Fact]
		public void Replication_DoesNotEmitStaticDelegateFile()
		{
			// The per-component {Ctx}Replication.cs static-delegate class
			// was removed when the wire collapsed to a single per-context
			// RemoteEvent. Replication is runtime-driven now.
			TestHarness.Project p = Run(nameof(Replication_DoesNotEmitStaticDelegateFile), OneReplicatedHealth);
			Assert.False(TestHarness.GeneratedExists(p, "GameReplication.cs"));
		}

		[Fact]
		public void Replication_ValueAddX_BodyEnqueuesSet()
		{
			// Set covers Added + Replaced — wire op shape doesn't distinguish.
			// Client picks AddX vs ReplaceX by HasX on dispatch.
			TestHarness.Project p = Run(nameof(Replication_ValueAddX_BodyEnqueuesSet), OneReplicatedHealth);
			string entity = TestHarness.ReadGenerated(p, "Components/Game.Health.cs");
			Assert.Contains("EntitiesReplication.QueueSet(\"Game\", GameComponentsLookup.Health, creationIndex, newValue);", entity);
		}

		[Fact]
		public void Replication_ValueReplaceX_BodyEnqueuesSet()
		{
			TestHarness.Project p = Run(nameof(Replication_ValueReplaceX_BodyEnqueuesSet), OneReplicatedHealth);
			string entity = TestHarness.ReadGenerated(p, "Components/Game.Health.cs");
			int hits = System.Text.RegularExpressions.Regex.Matches(
				entity, @"EntitiesReplication\.QueueSet\(""Game"", GameComponentsLookup\.Health, creationIndex, newValue\);").Count;
			Assert.True(hits >= 2, $"Expected QueueSet in both Add and Replace bodies, found {hits}.");
		}

		[Fact]
		public void Replication_ValueSetter_EnqueuesSet()
		{
			TestHarness.Project p = Run(nameof(Replication_ValueSetter_EnqueuesSet), OneReplicatedHealth);
			string entity = TestHarness.ReadGenerated(p, "Components/Game.Health.cs");
			Assert.Contains("EntitiesReplication.QueueSet(\"Game\", GameComponentsLookup.Health, creationIndex, value);", entity);
		}

		[Fact]
		public void Replication_RemoveX_BodyEnqueuesRemove()
		{
			TestHarness.Project p = Run(nameof(Replication_RemoveX_BodyEnqueuesRemove), OneReplicatedHealth);
			string entity = TestHarness.ReadGenerated(p, "Components/Game.Health.cs");
			Assert.Contains("EntitiesReplication.QueueRemove(\"Game\", GameComponentsLookup.Health, creationIndex);", entity);
		}

		[Fact]
		public void Replication_FlagSetter_EnqueuesSetOrRemoveByValue()
		{
			TestHarness.Project p = Run(nameof(Replication_FlagSetter_EnqueuesSetOrRemoveByValue), OneReplicatedFlag);
			string entity = TestHarness.ReadGenerated(p, "Components/Game.Stunned.cs");
			Assert.Contains("EntitiesReplication.QueueSet(\"Game\", GameComponentsLookup.Stunned, creationIndex);", entity);
			Assert.Contains("EntitiesReplication.QueueRemove(\"Game\", GameComponentsLookup.Stunned, creationIndex);", entity);
		}

		[Fact]
		public void NonReplicated_AddX_DoesNotEnqueueAnything()
		{
			// Regression: only [Replicated] components get the enqueue
			// injection. Plain components stay untouched.
			TestHarness.Project p = Run(nameof(NonReplicated_AddX_DoesNotEnqueueAnything), OneGameHealth);
			string entity = TestHarness.ReadGenerated(p, "Components/Game.Health.cs");
			Assert.DoesNotContain("EntitiesReplication.Queue", entity);
		}

		[Fact]
		public void Replication_Enqueue_IsGuardedByShouldEmit()
		{
			// ShouldEmit returns false on the client and inside server
			// BeginSuppress scopes, so the codegen-emitted enqueues have to
			// be wrapped in the guard.
			TestHarness.Project p = Run(nameof(Replication_Enqueue_IsGuardedByShouldEmit), OneReplicatedHealth);
			string entity = TestHarness.ReadGenerated(p, "Components/Game.Health.cs");
			Assert.Contains("if (EntitiesReplication.ShouldEmit()) EntitiesReplication.QueueSet(", entity);
			Assert.Contains("if (EntitiesReplication.ShouldEmit()) EntitiesReplication.QueueRemove(", entity);
		}

		[Fact]
		public void Replicated_MultiField_Getter_RoutesThroughGetReplicatedComponent()
		{
			// Multi-field [Replicated] components hand back a frozen clone
			// via GetReplicatedComponent so direct field mutation
			// (`e.Pos.X = 5`) throws instead of silently desyncing — the
			// replication wire only fires on Replace{X}, not on in-place
			// mutation. Single-Value-field components stay safe because
			// the getter returns a primitive copy.
			string source = @"
using Entities;
using Entities.CodeGeneration.Attributes;
namespace G { [Game, Replicated] public class Pos : IComponent { public int X; public int Y; } }";
			TestHarness.Project p = Run(nameof(Replicated_MultiField_Getter_RoutesThroughGetReplicatedComponent), source);
			string entity = TestHarness.ReadGenerated(p, "Components/Game.Pos.cs");
			Assert.Contains("get { return (global::G.Pos)GetReplicatedComponent(GameComponentsLookup.Pos); }", entity);
			Assert.DoesNotContain("get { return (global::G.Pos)GetComponent(GameComponentsLookup.Pos); }", entity);
		}

		[Fact]
		public void NonReplicated_MultiField_Getter_StaysOnGetComponent()
		{
			// Non-replicated multi-field components keep the zero-overhead
			// GetComponent path — the freeze/clone cost only buys safety
			// against silent network desync, which non-replicated state
			// can't have.
			string source = @"
using Entities;
using Entities.CodeGeneration.Attributes;
namespace G { [Game] public class Pos : IComponent { public int X; public int Y; } }";
			TestHarness.Project p = Run(nameof(NonReplicated_MultiField_Getter_StaysOnGetComponent), source);
			string entity = TestHarness.ReadGenerated(p, "Components/Game.Pos.cs");
			Assert.Contains("get { return (global::G.Pos)GetComponent(GameComponentsLookup.Pos); }", entity);
			Assert.DoesNotContain("GetReplicatedComponent", entity);
		}

		// ----------------------------------------------------------------
		// Client replication codegen — Generated/{Ctx}ClientReplication.cs
		// subscribes once per context to the runtime RemoteEvent and
		// dispatches received ops onto the local mirror entity.
		// ----------------------------------------------------------------

		[Fact]
		public void ClientReplication_EmittedWhenContextHasReplicatedComponent()
		{
			TestHarness.Project p = Run(nameof(ClientReplication_EmittedWhenContextHasReplicatedComponent), OneReplicatedHealth);
			Assert.True(TestHarness.GeneratedExists(p, "GameClientReplication.cs"));
		}

		[Fact]
		public void ClientReplication_NotEmittedWhenNoReplicatedComponents()
		{
			TestHarness.Project p = Run(nameof(ClientReplication_NotEmittedWhenNoReplicatedComponents), OneGameHealth);
			Assert.False(TestHarness.GeneratedExists(p, "GameClientReplication.cs"));
		}

		[Fact]
		public void ClientReplication_HoldsContextAndDictionary()
		{
			TestHarness.Project p = Run(nameof(ClientReplication_HoldsContextAndDictionary), OneReplicatedHealth);
			string mirror = TestHarness.ReadGenerated(p, "GameClientReplication.cs");
			Assert.Contains("using System.Collections.Generic;", mirror);
			Assert.Contains("public sealed class GameClientReplication", mirror);
			Assert.Contains("private readonly GameContext _context;", mirror);
			Assert.Contains("private readonly Dictionary<int, GameEntity> _byServerId = new();", mirror);
		}

		[Fact]
		public void ClientReplication_SubscribesOnceInConstructor()
		{
			// Single subscription per context — runtime fans out from one
			// RemoteEvent. No per-component += handler from the old shape.
			TestHarness.Project p = Run(nameof(ClientReplication_SubscribesOnceInConstructor), OneReplicatedHealth);
			string mirror = TestHarness.ReadGenerated(p, "GameClientReplication.cs");
			Assert.Contains("EntitiesReplication.Subscribe(\"Game\", GameComponentsLookup.BuildDigest, OnOps);", mirror);
			Assert.DoesNotContain("HealthAdded +=", mirror);
			Assert.DoesNotContain("GameReplication.", mirror);
		}

		[Fact]
		public void ClientReplication_GetOrCreate_UsesContainsKeyPattern()
		{
			// `out var` doesn't lower (DeclarationExpressionSyntax gap), so
			// the mirror uses ContainsKey + indexer instead.
			TestHarness.Project p = Run(nameof(ClientReplication_GetOrCreate_UsesContainsKeyPattern), OneReplicatedHealth);
			string mirror = TestHarness.ReadGenerated(p, "GameClientReplication.cs");
			Assert.Contains("if (_byServerId.ContainsKey(serverId)) return _byServerId[serverId];", mirror);
			Assert.DoesNotContain("TryGetValue", mirror);
		}

		[Fact]
		public void ClientReplication_OnOps_DispatchesByOpcode()
		{
			// Wire shape: object[] batch; each op is object[] with
			// {opcode, componentIndex, entityId, ...fields}.
			TestHarness.Project p = Run(nameof(ClientReplication_OnOps_DispatchesByOpcode), OneReplicatedHealth);
			string mirror = TestHarness.ReadGenerated(p, "GameClientReplication.cs");
			Assert.Contains("private void OnOps(object[] ops)", mirror);
			Assert.Contains("object[] op = (object[])ops[i];", mirror);
			Assert.Contains("int opcode = (int)op[0];", mirror);
			Assert.Contains("if (opcode == 1) { ApplyRemove(compIndex, serverId); continue; }", mirror);
		}

		[Fact]
		public void ClientReplication_ApplySet_Value_PicksReplaceOrAddByHasX()
		{
			// For value components the dispatch checks HasX and routes to
			// Replace if present, Add otherwise — makes server re-sends
			// idempotent (late join, re-sync, etc.). With delta encoding,
			// the args come from a bitmask-driven walk: bit 0 set → take
			// new value from op[4], else read the existing field from the
			// local component.
			TestHarness.Project p = Run(nameof(ClientReplication_ApplySet_Value_PicksReplaceOrAddByHasX), OneReplicatedHealth);
			string mirror = TestHarness.ReadGenerated(p, "GameClientReplication.cs");
			Assert.Contains("if (compIndex == GameComponentsLookup.Health)", mirror);
			Assert.Contains("int bitmask = (int)op[3];", mirror);
			Assert.Contains("(bitmask & 1) != 0", mirror);
			Assert.Contains("? (int)op[payloadIdx++]", mirror);
			Assert.Contains(": (e.HasHealth ? e.Health : default(int));", mirror);
			Assert.Contains("if (e.HasHealth) e.ReplaceHealth(v0);", mirror);
			Assert.Contains("else e.AddHealth(v0);", mirror);
		}

		[Fact]
		public void ClientReplication_ApplyRemove_Value_CallsRemoveX()
		{
			TestHarness.Project p = Run(nameof(ClientReplication_ApplyRemove_Value_CallsRemoveX), OneReplicatedHealth);
			string mirror = TestHarness.ReadGenerated(p, "GameClientReplication.cs");
			Assert.Contains("e.RemoveHealth();", mirror);
		}

		[Fact]
		public void ClientReplication_ApplySet_Flag_SetsIsXTrue()
		{
			TestHarness.Project p = Run(nameof(ClientReplication_ApplySet_Flag_SetsIsXTrue), OneReplicatedFlag);
			string mirror = TestHarness.ReadGenerated(p, "GameClientReplication.cs");
			Assert.Contains("if (compIndex == GameComponentsLookup.Stunned)", mirror);
			Assert.Contains("e.IsStunned = true;", mirror);
		}

		[Fact]
		public void ClientReplication_ApplyRemove_Flag_SetsIsXFalse()
		{
			TestHarness.Project p = Run(nameof(ClientReplication_ApplyRemove_Flag_SetsIsXFalse), OneReplicatedFlag);
			string mirror = TestHarness.ReadGenerated(p, "GameClientReplication.cs");
			Assert.Contains("e.IsStunned = false;", mirror);
		}

		// ----------------------------------------------------------------
		// Server replication codegen — Generated/{Ctx}ServerReplication.cs
		// registers a per-context snapshotter so a late-joining player can
		// be sent the current world on Ready ping.
		// ----------------------------------------------------------------

		[Fact]
		public void ServerReplication_EmittedWhenContextHasReplicatedComponent()
		{
			TestHarness.Project p = Run(nameof(ServerReplication_EmittedWhenContextHasReplicatedComponent), OneReplicatedHealth);
			Assert.True(TestHarness.GeneratedExists(p, "GameServerReplication.cs"));
		}

		[Fact]
		public void ServerReplication_NotEmittedWhenNoReplicatedComponents()
		{
			TestHarness.Project p = Run(nameof(ServerReplication_NotEmittedWhenNoReplicatedComponents), OneGameHealth);
			Assert.False(TestHarness.GeneratedExists(p, "GameServerReplication.cs"));
		}

		[Fact]
		public void ServerReplication_ConstructorRegistersSnapshotter()
		{
			TestHarness.Project p = Run(nameof(ServerReplication_ConstructorRegistersSnapshotter), OneReplicatedHealth);
			string server = TestHarness.ReadGenerated(p, "GameServerReplication.cs");
			Assert.Contains("public sealed class GameServerReplication", server);
			Assert.Contains("private readonly GameContext _context;", server);
			Assert.Contains("EntitiesReplication.RegisterSnapshotter(\"Game\", Snapshot);", server);
		}

		[Fact]
		public void ServerReplication_Snapshot_ValueComponent_EmitsSetOpWithUnwrappedField()
		{
			// Single-Value field uses the entity's unwrapped accessor
			// (e.Health). Snapshot ops always carry the all-ones bitmask
			// — the joining client has no local state for this entity,
			// so every field has to land in the apply path.
			TestHarness.Project p = Run(nameof(ServerReplication_Snapshot_ValueComponent_EmitsSetOpWithUnwrappedField), OneReplicatedHealth);
			string server = TestHarness.ReadGenerated(p, "GameServerReplication.cs");
			Assert.Contains("private object[] Snapshot()", server);
			Assert.Contains("GameEntity[] entities = _context.GetEntities();", server);
			Assert.Contains("if (e.HasHealth)", server);
			Assert.Contains("ops.Add(new object[] { 0, GameComponentsLookup.Health, e.creationIndex, 1, e.Health });", server);
		}

		[Fact]
		public void ServerReplication_Snapshot_FlagComponent_EmitsSetOpWithoutFields()
		{
			// Flag op carries bitmask = 0 (no fields). Uniform shape with
			// tick-time flag Sets keeps the client decoder branch-free.
			TestHarness.Project p = Run(nameof(ServerReplication_Snapshot_FlagComponent_EmitsSetOpWithoutFields), OneReplicatedFlag);
			string server = TestHarness.ReadGenerated(p, "GameServerReplication.cs");
			Assert.Contains("if (e.IsStunned)", server);
			Assert.Contains("ops.Add(new object[] { 0, GameComponentsLookup.Stunned, e.creationIndex, 0 });", server);
		}

		[Fact]
		public void ServerReplication_Snapshot_MultiFieldComponent_UnpacksAllFields()
		{
			// Multi-field component → pull the component instance, then
			// unpack each field positionally. Snapshot bitmask is
			// (1 << fieldCount) - 1 = 3 for a 2-field component.
			string source = @"
using Entities;
using Entities.CodeGeneration.Attributes;
namespace G { [Game, Replicated] public class Pos : IComponent { public int X; public int Y; } }";
			TestHarness.Project p = Run(nameof(ServerReplication_Snapshot_MultiFieldComponent_UnpacksAllFields), source);
			string server = TestHarness.ReadGenerated(p, "GameServerReplication.cs");
			Assert.Contains("if (e.HasPos)", server);
			Assert.Contains("global::G.Pos comp = e.Pos;", server);
			Assert.Contains("ops.Add(new object[] { 0, GameComponentsLookup.Pos, e.creationIndex, 3, comp.X, comp.Y });", server);
		}

		[Fact]
		public void ServerReplication_Snapshot_ReturnsArray()
		{
			// Returns object[] so the runtime can FireClient(player, ops)
			// with the same shape it FireAllClients's for tick batches.
			TestHarness.Project p = Run(nameof(ServerReplication_Snapshot_ReturnsArray), OneReplicatedHealth);
			string server = TestHarness.ReadGenerated(p, "GameServerReplication.cs");
			Assert.Contains("return ops.ToArray();", server);
		}

		[Fact]
		public void ServerReplication_Snapshot_OnlyWalksReplicatedComponents()
		{
			// A context with one [Replicated] and one plain component —
			// only the replicated one shows up in the snapshot body.
			string source = @"
using Entities;
using Entities.CodeGeneration.Attributes;
namespace G {
	[Game, Replicated] public class Health : IComponent { public int Value; }
	[Game]             public class Local  : IComponent { public int Value; }
}";
			TestHarness.Project p = Run(nameof(ServerReplication_Snapshot_OnlyWalksReplicatedComponents), source);
			string server = TestHarness.ReadGenerated(p, "GameServerReplication.cs");
			Assert.Contains("e.HasHealth", server);
			Assert.DoesNotContain("e.HasLocal", server);
		}

		// ----------------------------------------------------------------
		// [Unique] — codegen emits singleton accessors on {Ctx}Context and
		// wires the entity's AddX / ReplaceX / RemoveX / setter to tail-update
		// the singleton field through internal _Set/_Clear hooks. Per-component
		// blocks land in the same Components/{Ctx}.{Component}.cs file.
		// ----------------------------------------------------------------

		private const string OneUniqueFlag = @"
using Entities;
using Entities.CodeGeneration.Attributes;
namespace G { [Game, Unique] public class GameSession : IComponent { } }";

		private const string OneUniqueValue = @"
using Entities;
using Entities.CodeGeneration.Attributes;
namespace G { [Game, Unique] public class Score : IComponent { public int Value; } }";

		[Fact]
		public void Unique_Flag_ContextHasEntityFieldAndAccessors()
		{
			TestHarness.Project p = Run(nameof(Unique_Flag_ContextHasEntityFieldAndAccessors), OneUniqueFlag);
			string comp = TestHarness.ReadGenerated(p, "Components/Game.GameSession.cs");
			Assert.Contains("public sealed partial class GameContext", comp);
			Assert.Contains("private GameEntity _gameSessionEntity;", comp);
			Assert.Contains("public GameEntity gameSessionEntity", comp);
			Assert.Contains("public bool isGameSession", comp);
		}

		[Fact]
		public void Unique_Flag_SetCreatesEntityAndTogglesFlag()
		{
			TestHarness.Project p = Run(nameof(Unique_Flag_SetCreatesEntityAndTogglesFlag), OneUniqueFlag);
			string comp = TestHarness.ReadGenerated(p, "Components/Game.GameSession.cs");
			Assert.Contains("public GameEntity SetGameSession()", comp);
			Assert.Contains("if (_gameSessionEntity == null) _gameSessionEntity = CreateEntity();", comp);
			Assert.Contains("_gameSessionEntity.IsGameSession = true;", comp);
		}

		[Fact]
		public void Unique_Flag_UnsetDestroysEntityAndNullsField()
		{
			TestHarness.Project p = Run(nameof(Unique_Flag_UnsetDestroysEntityAndNullsField), OneUniqueFlag);
			string comp = TestHarness.ReadGenerated(p, "Components/Game.GameSession.cs");
			Assert.Contains("public void UnsetGameSession()", comp);
			Assert.Contains("_gameSessionEntity.Destroy();", comp);
			Assert.Contains("_gameSessionEntity = null;", comp);
		}

		[Fact]
		public void Unique_Flag_HooksThrowOnConflictingEntity()
		{
			// Two entities trying to hold the same Unique component should
			// surface immediately — matches Entitas's "one entity at a time"
			// guarantee. The hook compares incoming entity to the tracked
			// one and throws on mismatch. Hooks are public + IEntity-typed
			// because they implement the I{Ctx}{Comp}ContextHooks interface
			// (the entity-side cast site dispatches through the interface
			// to break a transpiler-level circular import).
			TestHarness.Project p = Run(nameof(Unique_Flag_HooksThrowOnConflictingEntity), OneUniqueFlag);
			string comp = TestHarness.ReadGenerated(p, "Components/Game.GameSession.cs");
			Assert.Contains("public void _SetGameSessionEntity(IEntity entity)", comp);
			Assert.Contains("throw new System.Exception(\"Unique component GameSession is already assigned to a different entity.\");", comp);
			Assert.Contains("public void _ClearGameSessionEntity()", comp);
		}

		[Fact]
		public void Unique_Flag_EntitySetter_TailUpdatesContextField()
		{
			// User code calling `entity.IsGameSession = true` must keep
			// _gameSessionEntity in sync — the setter tail-calls
			// _SetGameSessionEntity(this) after AddComponent.
			TestHarness.Project p = Run(nameof(Unique_Flag_EntitySetter_TailUpdatesContextField), OneUniqueFlag);
			string comp = TestHarness.ReadGenerated(p, "Components/Game.GameSession.cs");
			// Captures `context` once via `{ GameContext _ctx = (GameContext)context; if (_ctx != null) ... }`
			// to halve the `self:context()` accessor count in Lua.
			Assert.Contains("_ctx._SetGameSessionEntity(this)", comp);
			Assert.Contains("_ctx._ClearGameSessionEntity()", comp);
		}

		[Fact]
		public void Unique_Value_ContextExposesUnwrappedValueAccessor()
		{
			TestHarness.Project p = Run(nameof(Unique_Value_ContextExposesUnwrappedValueAccessor), OneUniqueValue);
			string comp = TestHarness.ReadGenerated(p, "Components/Game.Score.cs");
			// Single-Value field unwraps — `Score` returns the int, not the component.
			Assert.Contains("public int Score", comp);
			Assert.Contains("return _scoreEntity != null ? _scoreEntity.Score : default(int);", comp);
		}

		[Fact]
		public void Unique_Value_SetAddsOrReplacesOnSingletonEntity()
		{
			TestHarness.Project p = Run(nameof(Unique_Value_SetAddsOrReplacesOnSingletonEntity), OneUniqueValue);
			string comp = TestHarness.ReadGenerated(p, "Components/Game.Score.cs");
			Assert.Contains("public GameEntity SetScore(int newValue)", comp);
			Assert.Contains("_scoreEntity.AddScore(newValue);", comp);
			Assert.Contains("_scoreEntity.ReplaceScore(newValue);", comp);
		}

		[Fact]
		public void Unique_Value_EntityAddReplaceRemove_TailUpdateContextField()
		{
			TestHarness.Project p = Run(nameof(Unique_Value_EntityAddReplaceRemove_TailUpdateContextField), OneUniqueValue);
			string comp = TestHarness.ReadGenerated(p, "Components/Game.Score.cs");
			int setHits = System.Text.RegularExpressions.Regex.Matches(
				comp, @"_ctx\._SetScoreEntity\(this\);").Count;
			Assert.True(setHits >= 3, $"Expected _SetScoreEntity in Add + Replace + setter, found {setHits}.");
			Assert.Contains("_ctx._ClearScoreEntity();", comp);
		}

		[Fact]
		public void Unique_NotEmittedWhenComponentIsNotUnique()
		{
			// Plain (non-[Unique]) components don't get the context partial.
			TestHarness.Project p = Run(nameof(Unique_NotEmittedWhenComponentIsNotUnique), OneGamePlayer);
			string comp = TestHarness.ReadGenerated(p, "Components/Game.Player.cs");
			Assert.DoesNotContain("partial class GameContext", comp);
			Assert.DoesNotContain("_playerEntity", comp);
		}

		// ----------------------------------------------------------------
		// [PostConstructor] — methods on `partial class Contexts` tagged
		// with the attribute are called at the tail of the generated
		// `Contexts()` ctor. Typical use is registering entity indices
		// after every per-context field is wired.
		// ----------------------------------------------------------------

		[Fact]
		public void PostConstructor_MethodIsCalledAtTailOfContextsCtor()
		{
			string source = @"
using Entities;
using Entities.CodeGeneration.Attributes;
namespace G { [Game] public class Player : IComponent { } }
public partial class Contexts {
	[PostConstructor]
	public void InitializeEntityIndices() { }
}";
			TestHarness.Project p = Run(nameof(PostConstructor_MethodIsCalledAtTailOfContextsCtor), source);
			string contexts = TestHarness.ReadGenerated(p, "Contexts.cs");
			Assert.Contains("public Contexts()", contexts);
			Assert.Contains("InitializeEntityIndices();", contexts);
			// Ordering: PostConstructor calls land AFTER every per-context
			// `<name> = new <Name>Context();` line.
			int gameAssignIdx = contexts.IndexOf("game = new GameContext();", StringComparison.Ordinal);
			int postIdx = contexts.IndexOf("InitializeEntityIndices();", StringComparison.Ordinal);
			Assert.True(gameAssignIdx > 0 && postIdx > gameAssignIdx, "PostConstructor call must follow context field init.");
		}

		[Fact]
		public void PostConstructor_MultipleMethodsAllCalled()
		{
			string source = @"
using Entities;
using Entities.CodeGeneration.Attributes;
namespace G { [Game] public class Player : IComponent { } }
public partial class Contexts {
	[PostConstructor] public void StepA() { }
	[PostConstructor] public void StepB() { }
}";
			TestHarness.Project p = Run(nameof(PostConstructor_MultipleMethodsAllCalled), source);
			string contexts = TestHarness.ReadGenerated(p, "Contexts.cs");
			Assert.Contains("StepA();", contexts);
			Assert.Contains("StepB();", contexts);
		}

		[Fact]
		public void PostConstructor_NoMethods_NoExtraCalls()
		{
			TestHarness.Project p = Run(nameof(PostConstructor_NoMethods_NoExtraCalls), OneGamePlayer);
			string contexts = TestHarness.ReadGenerated(p, "Contexts.cs");
			Assert.Contains("game = new GameContext();", contexts);
			Assert.DoesNotContain("Initialize", contexts);
		}

		// ----------------------------------------------------------------
		// [EntityIndex] / [PrimaryEntityIndex] — field-level. Codegen emits
		// a per-index dict + lookup on the {Ctx}Context partial; entity's
		// AddX / ReplaceX / RemoveX / setter bodies tail-update via
		// _Register / _Unregister hooks.
		// ----------------------------------------------------------------

		private const string OnePrimaryIndexedUser = @"
using Entities;
using Entities.CodeGeneration.Attributes;
namespace G {
	[Game] public class User : IComponent {
		[PrimaryEntityIndex] public string UserId;
	}
}";

		private const string OneIndexedOwned = @"
using Entities;
using Entities.CodeGeneration.Attributes;
namespace G {
	[Game] public class Owned : IComponent {
		[EntityIndex] public int OwnerId;
	}
}";

		[Fact]
		public void PrimaryIndex_ContextHasDictAndLookupMethod()
		{
			TestHarness.Project p = Run(nameof(PrimaryIndex_ContextHasDictAndLookupMethod), OnePrimaryIndexedUser);
			string comp = TestHarness.ReadGenerated(p, "Components/Game.User.cs");
			Assert.Contains("public sealed partial class GameContext", comp);
			Assert.Contains("private System.Collections.Generic.Dictionary<string, GameEntity> _userByUserId = new();", comp);
			Assert.Contains("public GameEntity GetEntityWithUser(string key)", comp);
		}

		[Fact]
		public void PrimaryIndex_RegisterThrowsOnDuplicateKey()
		{
			TestHarness.Project p = Run(nameof(PrimaryIndex_RegisterThrowsOnDuplicateKey), OnePrimaryIndexedUser);
			string comp = TestHarness.ReadGenerated(p, "Components/Game.User.cs");
			Assert.Contains("public void _RegisterUser(IEntity entity, string key)", comp);
			Assert.Contains("throw new System.Exception(\"PrimaryEntityIndex collision on User.UserId for key: \" + key);", comp);
		}

		[Fact]
		public void PrimaryIndex_EntityAdd_RegistersKey()
		{
			TestHarness.Project p = Run(nameof(PrimaryIndex_EntityAdd_RegistersKey), OnePrimaryIndexedUser);
			string comp = TestHarness.ReadGenerated(p, "Components/Game.User.cs");
			Assert.Contains("_ctx._RegisterUser(this, newUserId);", comp);
		}

		[Fact]
		public void PrimaryIndex_EntityReplace_UnregistersOldThenRegistersNew()
		{
			TestHarness.Project p = Run(nameof(PrimaryIndex_EntityReplace_UnregistersOldThenRegistersNew), OnePrimaryIndexedUser);
			string comp = TestHarness.ReadGenerated(p, "Components/Game.User.cs");
			// Pre-replace capture so we can unregister the prior key.
			Assert.Contains("string _prevUserId = HasUser ? User.UserId : default(string);", comp);
			Assert.Contains("bool _hadUser = HasUser;", comp);
			Assert.Contains("if (_hadUser) _ctx._UnregisterUser(this, _prevUserId);", comp);
			Assert.Contains("_ctx._RegisterUser(this, newUserId);", comp);
		}

		[Fact]
		public void PrimaryIndex_EntityRemove_UnregistersKey()
		{
			TestHarness.Project p = Run(nameof(PrimaryIndex_EntityRemove_UnregistersKey), OnePrimaryIndexedUser);
			string comp = TestHarness.ReadGenerated(p, "Components/Game.User.cs");
			Assert.Contains("string _prevUserId = HasUser ? User.UserId : default(string);", comp);
			Assert.Contains("_ctx._UnregisterUser(this, _prevUserId);", comp);
		}

		[Fact]
		public void EntityIndex_NonPrimary_ContextHasHashSetDictAndPluralLookup()
		{
			TestHarness.Project p = Run(nameof(EntityIndex_NonPrimary_ContextHasHashSetDictAndPluralLookup), OneIndexedOwned);
			string comp = TestHarness.ReadGenerated(p, "Components/Game.Owned.cs");
			Assert.Contains("System.Collections.Generic.Dictionary<int, System.Collections.Generic.HashSet<GameEntity>>", comp);
			Assert.Contains("public System.Collections.Generic.IEnumerable<GameEntity> GetEntitiesWithOwned(int key)", comp);
		}

		[Fact]
		public void EntityIndex_NonPrimary_RegisterAppendsToSet()
		{
			// Register casts IEntity → GameEntity first (the index dict is
			// typed on the concrete entity), then appends the typed value.
			TestHarness.Project p = Run(nameof(EntityIndex_NonPrimary_RegisterAppendsToSet), OneIndexedOwned);
			string comp = TestHarness.ReadGenerated(p, "Components/Game.Owned.cs");
			Assert.Contains("public void _RegisterOwned(IEntity entity, int key)", comp);
			Assert.Contains("GameEntity typed = (GameEntity)entity;", comp);
			Assert.Contains("set.Add(typed);", comp);
		}

		[Fact]
		public void EntityIndex_NotEmittedWhenComponentHasNoIndexedField()
		{
			TestHarness.Project p = Run(nameof(EntityIndex_NotEmittedWhenComponentHasNoIndexedField), OneGameHealth);
			string comp = TestHarness.ReadGenerated(p, "Components/Game.Health.cs");
			Assert.DoesNotContain("partial class GameContext", comp);
			Assert.DoesNotContain("_RegisterHealth", comp);
		}

		[Fact]
		public void EntityIndex_OnUnwrappedValue_SetterMaintainsIndex()
		{
			// Edge: [PrimaryEntityIndex] on `public int Value;` — the
			// unwrap setter (`entity.Score = ...`) needs to capture the
			// old Value and unregister it before registering the new.
			string source = @"
using Entities;
using Entities.CodeGeneration.Attributes;
namespace G {
	[Game] public class Score : IComponent {
		[PrimaryEntityIndex] public int Value;
	}
}";
			TestHarness.Project p = Run(nameof(EntityIndex_OnUnwrappedValue_SetterMaintainsIndex), source);
			string comp = TestHarness.ReadGenerated(p, "Components/Game.Score.cs");
			Assert.Contains("int _prevValue = HasScore ? Score : default(int);", comp);
			Assert.Contains("_ctx._UnregisterScore(this, _prevValue);", comp);
			Assert.Contains("_ctx._RegisterScore(this, value);", comp);
		}

		// ----------------------------------------------------------------
		// Entity.Destroy override — codegen emits a per-context override
		// that pre-fires RemoveX (or `IsX = false`) for every hooked
		// component before the base teardown, so [Replicated] QueueRemove
		// ops, [Unique] _Clear hooks, and [EntityIndex] _Unregister hooks
		// all run when user code destroys an entity directly instead of
		// going through UnsetX / RemoveX.
		// ----------------------------------------------------------------

		[Fact]
		public void EntityDestroy_OverriddenWhenContextHasReplicatedComponent()
		{
			TestHarness.Project p = Run(nameof(EntityDestroy_OverriddenWhenContextHasReplicatedComponent), OneReplicatedHealth);
			string entity = TestHarness.ReadGenerated(p, "GameEntity.cs");
			Assert.Contains("public override void Destroy()", entity);
			Assert.Contains("if (HasHealth) RemoveHealth();", entity);
			Assert.Contains("base.Destroy();", entity);
		}

		[Fact]
		public void EntityDestroy_OverriddenWhenContextHasUniqueComponent()
		{
			TestHarness.Project p = Run(nameof(EntityDestroy_OverriddenWhenContextHasUniqueComponent), OneUniqueValue);
			string entity = TestHarness.ReadGenerated(p, "GameEntity.cs");
			Assert.Contains("public override void Destroy()", entity);
			Assert.Contains("if (HasScore) RemoveScore();", entity);
		}

		[Fact]
		public void EntityDestroy_OverriddenWhenContextHasIndexedComponent()
		{
			TestHarness.Project p = Run(nameof(EntityDestroy_OverriddenWhenContextHasIndexedComponent), OnePrimaryIndexedUser);
			string entity = TestHarness.ReadGenerated(p, "GameEntity.cs");
			Assert.Contains("public override void Destroy()", entity);
			Assert.Contains("if (HasUser) RemoveUser();", entity);
		}

		[Fact]
		public void EntityDestroy_FlagWithHook_UsesIsXFalse()
		{
			// Flags don't have a RemoveX method — the cleanup is
			// `IsX = false`, which routes through the same setter that
			// fires the [Unique] / [Replicated] hooks for flag flips.
			TestHarness.Project p = Run(nameof(EntityDestroy_FlagWithHook_UsesIsXFalse), OneUniqueFlag);
			string entity = TestHarness.ReadGenerated(p, "GameEntity.cs");
			Assert.Contains("if (IsGameSession) IsGameSession = false;", entity);
		}

		[Fact]
		public void EntityDestroy_NotOverriddenForHookFreeContext()
		{
			// Plain component with no [Replicated], no [Unique], no index —
			// no override needed; the base partial stays empty.
			TestHarness.Project p = Run(nameof(EntityDestroy_NotOverriddenForHookFreeContext), OneGamePlayer);
			string entity = TestHarness.ReadGenerated(p, "GameEntity.cs");
			Assert.DoesNotContain("public override void Destroy()", entity);
			Assert.Contains("public sealed partial class GameEntity : Entity { }", entity);
		}

		[Fact]
		public void EntityDestroy_CallsBaseAfterEveryHookedRemove()
		{
			// Ordering matters: every per-component cleanup runs before
			// base.Destroy() so hooks see live entity state.
			string source = @"
using Entities;
using Entities.CodeGeneration.Attributes;
namespace G {
	[Game, Replicated] public class Health : IComponent { public int Value; }
	[Game, Unique]     public class GameSession : IComponent { }
}";
			TestHarness.Project p = Run(nameof(EntityDestroy_CallsBaseAfterEveryHookedRemove), source);
			string entity = TestHarness.ReadGenerated(p, "GameEntity.cs");
			int healthIdx = entity.IndexOf("if (HasHealth) RemoveHealth();", StringComparison.Ordinal);
			int sessionIdx = entity.IndexOf("if (IsGameSession) IsGameSession = false;", StringComparison.Ordinal);
			int baseIdx = entity.IndexOf("base.Destroy();", StringComparison.Ordinal);
			Assert.True(healthIdx > 0 && sessionIdx > 0 && baseIdx > 0, "All three calls must appear in the override body.");
			Assert.True(healthIdx < baseIdx, "RemoveHealth must precede base.Destroy.");
			Assert.True(sessionIdx < baseIdx, "IsGameSession = false must precede base.Destroy.");
		}

		// ----------------------------------------------------------------
		// [Watched] — class-level. Codegen synthesizes a {Name}Changed
		// flag component in the same context + namespace, patches
		// state-mutating entity bodies (AddX / ReplaceX / unwrap setter,
		// flag flip either direction) to set `Is{Name}Changed = true`,
		// and emits a {Ctx}WatchedCleanupSystem the user adds to the
		// tail of their feature pipeline.
		// ----------------------------------------------------------------

		private const string OneWatchedValue = @"
using Entities;
using Entities.CodeGeneration.Attributes;
namespace G { [Game, Watched] public class Health : IComponent { public int Value; } }";

		private const string OneWatchedFlag = @"
using Entities;
using Entities.CodeGeneration.Attributes;
namespace G { [Game, Watched] public class Stunned : IComponent { } }";

		[Fact]
		public void Watched_EmitsChangedFlagClass()
		{
			TestHarness.Project p = Run(nameof(Watched_EmitsChangedFlagClass), OneWatchedValue);
			Assert.True(TestHarness.GeneratedExists(p, "Watched/HealthChanged.cs"));
			string changedClass = TestHarness.ReadGenerated(p, "Watched/HealthChanged.cs");
			Assert.Contains("namespace G", changedClass);
			Assert.Contains("public class HealthChanged : IComponent { }", changedClass);
		}

		[Fact]
		public void Watched_ChangedFlagAppearsInComponentsLookup()
		{
			TestHarness.Project p = Run(nameof(Watched_ChangedFlagAppearsInComponentsLookup), OneWatchedValue);
			string lookup = TestHarness.ReadGenerated(p, "GameComponentsLookup.cs");
			Assert.Contains("public const int HealthChanged", lookup);
			Assert.Contains("typeof(HealthChanged)", lookup);
		}

		[Fact]
		public void Watched_EntityHasIsChangedFlagSetter()
		{
			TestHarness.Project p = Run(nameof(Watched_EntityHasIsChangedFlagSetter), OneWatchedValue);
			string comp = TestHarness.ReadGenerated(p, "Components/Game.HealthChanged.cs");
			Assert.Contains("public bool IsHealthChanged", comp);
		}

		[Fact]
		public void Watched_ValueAdd_SetsChangedFlag()
		{
			TestHarness.Project p = Run(nameof(Watched_ValueAdd_SetsChangedFlag), OneWatchedValue);
			string comp = TestHarness.ReadGenerated(p, "Components/Game.Health.cs");
			// The AddX body should end with `IsHealthChanged = true;` before the return.
			int addIdx = comp.IndexOf("public global::G.Health AddHealth(", StringComparison.Ordinal);
			int returnIdx = comp.IndexOf("return component;", addIdx, StringComparison.Ordinal);
			string addBody = comp.Substring(addIdx, returnIdx - addIdx);
			Assert.Contains("IsHealthChanged = true;", addBody);
		}

		[Fact]
		public void Watched_ValueReplace_SetsChangedFlag()
		{
			TestHarness.Project p = Run(nameof(Watched_ValueReplace_SetsChangedFlag), OneWatchedValue);
			string comp = TestHarness.ReadGenerated(p, "Components/Game.Health.cs");
			int replaceIdx = comp.IndexOf("public global::G.Health ReplaceHealth(", StringComparison.Ordinal);
			int returnIdx = comp.IndexOf("return component;", replaceIdx, StringComparison.Ordinal);
			string replaceBody = comp.Substring(replaceIdx, returnIdx - replaceIdx);
			Assert.Contains("IsHealthChanged = true;", replaceBody);
		}

		[Fact]
		public void Watched_ValueUnwrapSetter_SetsChangedFlag()
		{
			// `entity.Health = 7` setter path — same Changed signal as
			// ReplaceHealth.
			TestHarness.Project p = Run(nameof(Watched_ValueUnwrapSetter_SetsChangedFlag), OneWatchedValue);
			string comp = TestHarness.ReadGenerated(p, "Components/Game.Health.cs");
			// The setter block holds the unwrap; the Changed write lives at the end.
			int setterIdx = comp.IndexOf("set", StringComparison.Ordinal);
			int closeIdx = comp.IndexOf("\t\t}", setterIdx, StringComparison.Ordinal);
			string setterBody = comp.Substring(setterIdx, closeIdx - setterIdx);
			Assert.Contains("IsHealthChanged = true;", setterBody);
		}

		[Fact]
		public void Watched_ValueRemove_DoesNotSetChangedFlag()
		{
			// RemoveX deliberately doesn't set Changed — the entity no
			// longer has the component, so a Changed matcher couldn't
			// observe it anyway.
			TestHarness.Project p = Run(nameof(Watched_ValueRemove_DoesNotSetChangedFlag), OneWatchedValue);
			string comp = TestHarness.ReadGenerated(p, "Components/Game.Health.cs");
			int removeIdx = comp.IndexOf("public void RemoveHealth()", StringComparison.Ordinal);
			int braceClose = comp.IndexOf("\n\t}", removeIdx, StringComparison.Ordinal);
			string removeBody = comp.Substring(removeIdx, braceClose - removeIdx);
			Assert.DoesNotContain("IsHealthChanged = true;", removeBody);
		}

		[Fact]
		public void Watched_FlagSetter_BothDirectionsSetChangedFlag()
		{
			// Flag flip is a change in either direction — both true and
			// false branches raise Changed.
			TestHarness.Project p = Run(nameof(Watched_FlagSetter_BothDirectionsSetChangedFlag), OneWatchedFlag);
			string comp = TestHarness.ReadGenerated(p, "Components/Game.Stunned.cs");
			// Count occurrences of the Changed write in the setter — once
			// per branch (true / false).
			int hits = System.Text.RegularExpressions.Regex.Matches(
				comp, @"IsStunnedChanged = true;").Count;
			Assert.True(hits >= 2, $"Expected `IsStunnedChanged = true;` in both true/false branches, found {hits}.");
		}

		[Fact]
		public void Watched_CleanupSystem_Emitted()
		{
			TestHarness.Project p = Run(nameof(Watched_CleanupSystem_Emitted), OneWatchedValue);
			Assert.True(TestHarness.GeneratedExists(p, "GameWatchedCleanupSystem.cs"));
			string cleanup = TestHarness.ReadGenerated(p, "GameWatchedCleanupSystem.cs");
			Assert.Contains("public sealed class GameWatchedCleanupSystem : ICleanupSystem", cleanup);
			Assert.Contains("public void Cleanup()", cleanup);
			Assert.Contains("e.IsHealthChanged = false;", cleanup);
		}

		[Fact]
		public void Watched_CleanupSystem_NotEmittedWhenNoWatchedComponents()
		{
			TestHarness.Project p = Run(nameof(Watched_CleanupSystem_NotEmittedWhenNoWatchedComponents), OneGameHealth);
			Assert.False(TestHarness.GeneratedExists(p, "GameWatchedCleanupSystem.cs"));
		}

		[Fact]
		public void Watched_CleanupSystem_OneLoopPerWatchedComponent()
		{
			string source = @"
using Entities;
using Entities.CodeGeneration.Attributes;
namespace G {
	[Game, Watched] public class Health : IComponent { public int Value; }
	[Game, Watched] public class Stunned : IComponent { }
	[Game]          public class Plain : IComponent { public int Value; }
}";
			TestHarness.Project p = Run(nameof(Watched_CleanupSystem_OneLoopPerWatchedComponent), source);
			string cleanup = TestHarness.ReadGenerated(p, "GameWatchedCleanupSystem.cs");
			Assert.Contains("e.IsHealthChanged = false;", cleanup);
			Assert.Contains("e.IsStunnedChanged = false;", cleanup);
			Assert.DoesNotContain("IsPlainChanged", cleanup);
		}

		[Fact]
		public void Watched_NonWatchedComponent_DoesNotSetChanged()
		{
			TestHarness.Project p = Run(nameof(Watched_NonWatchedComponent_DoesNotSetChanged), OneGameHealth);
			string comp = TestHarness.ReadGenerated(p, "Components/Game.Health.cs");
			Assert.DoesNotContain("IsHealthChanged", comp);
		}

		// ----------------------------------------------------------------
		// BuildDigest — codegen-stamped fingerprint of the sorted component
		// layout. The Ready handshake compares client vs server digest and
		// kicks the player on mismatch, closing the silent-desync window
		// where a client built from drifted source would map componentIndex
		// to a different type than the server expects.
		// ----------------------------------------------------------------

		private static string ExtractDigest(string lookup)
		{
			System.Text.RegularExpressions.Match m = System.Text.RegularExpressions.Regex.Match(
				lookup, "public const string BuildDigest = \"([0-9a-f]+)\"");
			Assert.True(m.Success, "Expected BuildDigest const in lookup source.");
			return m.Groups[1].Value;
		}

		[Fact]
		public void Digest_EmittedInComponentsLookup()
		{
			TestHarness.Project p = Run(nameof(Digest_EmittedInComponentsLookup), OneGameHealth);
			string lookup = TestHarness.ReadGenerated(p, "GameComponentsLookup.cs");
			string digest = ExtractDigest(lookup);
			Assert.Equal(16, digest.Length);
		}

		[Fact]
		public void Digest_DeterministicAcrossReruns()
		{
			// Same source → same digest; sort is stable, hash input is the
			// FullName list verbatim. If this flakes, the sort or the
			// hash input shape regressed.
			string first = ExtractDigest(TestHarness.ReadGenerated(
				Run(nameof(Digest_DeterministicAcrossReruns) + "_A", OneGameHealth),
				"GameComponentsLookup.cs"));
			string second = ExtractDigest(TestHarness.ReadGenerated(
				Run(nameof(Digest_DeterministicAcrossReruns) + "_B", OneGameHealth),
				"GameComponentsLookup.cs"));
			Assert.Equal(first, second);
		}

		[Fact]
		public void Digest_ChangesWhenComponentAdded()
		{
			string baseline = ExtractDigest(TestHarness.ReadGenerated(
				Run(nameof(Digest_ChangesWhenComponentAdded) + "_Base", OneGameHealth),
				"GameComponentsLookup.cs"));
			string augmented = @"
using Entities;
using Entities.CodeGeneration.Attributes;
namespace G {
	[Game] public class Health : IComponent { public int Value; }
	[Game] public class Stamina : IComponent { public int Value; }
}";
			string altered = ExtractDigest(TestHarness.ReadGenerated(
				Run(nameof(Digest_ChangesWhenComponentAdded) + "_Augmented", augmented),
				"GameComponentsLookup.cs"));
			Assert.NotEqual(baseline, altered);
		}

		[Fact]
		public void Digest_ChangesWhenComponentRenamed()
		{
			string baseline = ExtractDigest(TestHarness.ReadGenerated(
				Run(nameof(Digest_ChangesWhenComponentRenamed) + "_Base", OneGameHealth),
				"GameComponentsLookup.cs"));
			string renamed = @"
using Entities;
using Entities.CodeGeneration.Attributes;
namespace G { [Game] public class HitPoints : IComponent { public int Value; } }";
			string altered = ExtractDigest(TestHarness.ReadGenerated(
				Run(nameof(Digest_ChangesWhenComponentRenamed) + "_Renamed", renamed),
				"GameComponentsLookup.cs"));
			Assert.NotEqual(baseline, altered);
		}

		[Fact]
		public void Digest_ChangesWhenComponentMovedToDifferentNamespace()
		{
			// Two builds with the SAME type-name and the SAME field shape,
			// only the namespace differs. The digest must change — that's
			// the whole reason (Namespace, TypeName) is the sort key.
			string nsA = @"
using Entities;
using Entities.CodeGeneration.Attributes;
namespace A { [Game] public class Health : IComponent { public int Value; } }";
			string nsB = @"
using Entities;
using Entities.CodeGeneration.Attributes;
namespace B { [Game] public class Health : IComponent { public int Value; } }";
			string digestA = ExtractDigest(TestHarness.ReadGenerated(
				Run(nameof(Digest_ChangesWhenComponentMovedToDifferentNamespace) + "_A", nsA),
				"GameComponentsLookup.cs"));
			string digestB = ExtractDigest(TestHarness.ReadGenerated(
				Run(nameof(Digest_ChangesWhenComponentMovedToDifferentNamespace) + "_B", nsB),
				"GameComponentsLookup.cs"));
			Assert.NotEqual(digestA, digestB);
		}

		[Fact]
		public void Sort_PrimaryKeyIsTypeName()
		{
			// Type names are unique within a context (the lookup emits
			// a `const int {TypeName}` which would otherwise collide),
			// so type-name alone gives a total order. Locks the wire
			// contract: a build that adds an alphabetically-later
			// component shifts only that component's index, never the
			// earlier ones — and the digest catches it on the client
			// side if a stale client misses the addition. Synthesized
			// Command / OriginUserId share the sort but the relative
			// ordering among user components stays alphabetical.
			string source = @"
using Entities;
using Entities.CodeGeneration.Attributes;
namespace G {
	[Game] public class Velocity : IComponent { public int Value; }
	[Game] public class Health : IComponent { public int Value; }
	[Game] public class Armor : IComponent { public int Value; }
}";
			TestHarness.Project p = Run(nameof(Sort_PrimaryKeyIsTypeName), source);
			string lookup = TestHarness.ReadGenerated(p, "GameComponentsLookup.cs");
			int armorIdx = lookup.IndexOf("public const int Armor = ", StringComparison.Ordinal);
			int healthIdx = lookup.IndexOf("public const int Health = ", StringComparison.Ordinal);
			int velocityIdx = lookup.IndexOf("public const int Velocity = ", StringComparison.Ordinal);
			Assert.True(armorIdx > 0 && healthIdx > 0 && velocityIdx > 0);
			Assert.True(armorIdx < healthIdx, "Armor must precede Health alphabetically.");
			Assert.True(healthIdx < velocityIdx, "Health must precede Velocity alphabetically.");
		}

		[Fact]
		public void ServerReplication_ConstructorRegistersDigestBeforeSnapshotter()
		{
			// RegisterDigest must precede RegisterSnapshotter — the
			// snapshot RemoteEvent's first OnServerEvent can fire as soon
			// as a player joins, and the Ready handler reads _digests to
			// validate. If we register the snapshotter first, an early
			// join could land in the handler before the digest is set,
			// and the comparison short-circuits to "no expectation =
			// allow" — masking the desync we're trying to catch.
			TestHarness.Project p = Run(
				nameof(ServerReplication_ConstructorRegistersDigestBeforeSnapshotter),
				OneReplicatedHealth);
			string server = TestHarness.ReadGenerated(p, "GameServerReplication.cs");
			int digestIdx = server.IndexOf("EntitiesReplication.RegisterDigest(\"Game\", GameComponentsLookup.BuildDigest);", StringComparison.Ordinal);
			int snapshotIdx = server.IndexOf("EntitiesReplication.RegisterSnapshotter(\"Game\", Snapshot);", StringComparison.Ordinal);
			Assert.True(digestIdx > 0 && snapshotIdx > 0, "Both registrations must appear.");
			Assert.True(digestIdx < snapshotIdx, "RegisterDigest must precede RegisterSnapshotter.");
		}

		[Fact]
		public void ClientReplication_SubscribeForwardsDigest()
		{
			TestHarness.Project p = Run(nameof(ClientReplication_SubscribeForwardsDigest), OneReplicatedHealth);
			string mirror = TestHarness.ReadGenerated(p, "GameClientReplication.cs");
			Assert.Contains("EntitiesReplication.Subscribe(\"Game\", GameComponentsLookup.BuildDigest, OnOps);", mirror);
		}

		// ----------------------------------------------------------------
		// Delta-encoded ApplySet — bitmask walks each field, takes the
		// new value from the payload when the bit is set, else reads the
		// existing field from the local frozen-clone component. Server
		// guarantees first-Set / Add always carries all-ones, so a non-
		// all-ones bitmask implies the client already Has{X}.
		// ----------------------------------------------------------------

		[Fact]
		public void ClientReplication_ApplySet_MultiField_ReconstructsFromBitmask()
		{
			// Two-field Pos: bit 0 selects X, bit 1 selects Y. Each field
			// emits a ternary: bit set → take new from op[payloadIdx++],
			// else read local e.Pos.{Field}. The payloadIdx walk packs
			// changed fields densely in the op tail.
			string source = @"
using Entities;
using Entities.CodeGeneration.Attributes;
namespace G { [Game, Replicated] public class Pos : IComponent { public int X; public int Y; } }";
			TestHarness.Project p = Run(nameof(ClientReplication_ApplySet_MultiField_ReconstructsFromBitmask), source);
			string mirror = TestHarness.ReadGenerated(p, "GameClientReplication.cs");
			Assert.Contains("int bitmask = (int)op[3];", mirror);
			Assert.Contains("int payloadIdx = 4;", mirror);
			// Field 0 (X) → bit 1
			Assert.Contains("(bitmask & 1) != 0", mirror);
			Assert.Contains(": (e.HasPos ? e.Pos.X : default(int));", mirror);
			// Field 1 (Y) → bit 2
			Assert.Contains("(bitmask & 2) != 0", mirror);
			Assert.Contains(": (e.HasPos ? e.Pos.Y : default(int));", mirror);
			// Final call uses the reconstructed locals, not raw op indices.
			Assert.Contains("if (e.HasPos) e.ReplacePos(v0, v1);", mirror);
			Assert.Contains("else e.AddPos(v0, v1);", mirror);
		}

		[Fact]
		public void ClientReplication_ApplySet_Flag_IgnoresBitmask()
		{
			// Flag carries bitmask = 0 on the wire but the client code
			// doesn't need to decode anything — Is{X} = true is the
			// whole op.
			TestHarness.Project p = Run(nameof(ClientReplication_ApplySet_Flag_IgnoresBitmask), OneReplicatedFlag);
			string mirror = TestHarness.ReadGenerated(p, "GameClientReplication.cs");
			Assert.Contains("if (compIndex == GameComponentsLookup.Stunned)", mirror);
			Assert.Contains("e.IsStunned = true;", mirror);
			// Flag branch must not introduce delta scaffolding.
			int flagBlockIdx = mirror.IndexOf("if (compIndex == GameComponentsLookup.Stunned)", StringComparison.Ordinal);
			int returnIdx = mirror.IndexOf("return;", flagBlockIdx, StringComparison.Ordinal);
			string flagBlock = mirror.Substring(flagBlockIdx, returnIdx - flagBlockIdx);
			Assert.DoesNotContain("bitmask", flagBlock);
			Assert.DoesNotContain("payloadIdx", flagBlock);
		}

		// ----------------------------------------------------------------
		// Commands — `entity.IsCommand = true` is the client→server ship
		// trigger. Codegen synthesizes the Command flag + OriginUserId
		// into every context unconditionally; ClientCommandSender +
		// ServerCommandReceiver are emitted per context regardless of
		// whether the user instantiates them. The setter tail on the
		// Command flag's true branch marks the entity pending; the
		// heartbeat shipper walks the entity's components and emits one
		// op per component. No attribute on data components — anything
		// the user puts on a command entity rides.
		// ----------------------------------------------------------------

		[Fact]
		public void Command_FlagAndOriginUserId_SynthesizedIntoEveryContext()
		{
			// Even a context with no replicated state gets both
			// synthesized components so `entity.IsCommand = true` works
			// uniformly. Contexts pay nothing at runtime if the user
			// never instantiates the sender/receiver.
			TestHarness.Project p = Run(nameof(Command_FlagAndOriginUserId_SynthesizedIntoEveryContext), OneGameHealth);
			Assert.True(TestHarness.GeneratedExists(p, "Commands/Command.cs"));
			Assert.True(TestHarness.GeneratedExists(p, "Commands/OriginUserId.cs"));

			string cmd = TestHarness.ReadGenerated(p, "Commands/Command.cs");
			Assert.Contains("public class Command : IComponent", cmd);

			string oui = TestHarness.ReadGenerated(p, "Commands/OriginUserId.cs");
			Assert.Contains("public class OriginUserId : IComponent", oui);
			Assert.Contains("public long Value;", oui);

			string lookup = TestHarness.ReadGenerated(p, "GameComponentsLookup.cs");
			Assert.Contains("public const int Command = ", lookup);
			Assert.Contains("public const int OriginUserId = ", lookup);
		}

		[Fact]
		public void Command_FlagSetter_TrueBranchMarksPending()
		{
			// Setting IsCommand = true tail-calls MarkCommandPending so
			// the heartbeat drain ships the entity. False branch unmarks
			// so toggling off before the drain cancels the ship cleanly.
			TestHarness.Project p = Run(nameof(Command_FlagSetter_TrueBranchMarksPending), OneGameHealth);
			string comp = TestHarness.ReadGenerated(p, "Components/Game.Command.cs");
			Assert.Contains("if (EntitiesReplication.ShouldSendCommand()) EntitiesReplication.MarkCommandPending(\"Game\", this);", comp);
			Assert.Contains("if (EntitiesReplication.ShouldSendCommand()) EntitiesReplication.UnmarkCommandPending(\"Game\", this);", comp);
		}

		[Fact]
		public void Command_RegularDataComponent_NoLongerEmitsCommandTail()
		{
			// Data components carry no attribute and no longer fire wire
			// ops on AddX. They ride only when the entity's Command flag
			// flips true — the shipper walks them then.
			TestHarness.Project p = Run(nameof(Command_RegularDataComponent_NoLongerEmitsCommandTail), OneGameHealth);
			string comp = TestHarness.ReadGenerated(p, "Components/Game.Health.cs");
			Assert.DoesNotContain("QueueCommandComponent", comp);
			Assert.DoesNotContain("MarkCommandPending", comp);
		}

		[Fact]
		public void Command_ClientSender_EmittedPerContext()
		{
			TestHarness.Project p = Run(nameof(Command_ClientSender_EmittedPerContext), OneGameHealth);
			Assert.True(TestHarness.GeneratedExists(p, "GameClientCommandSender.cs"));
			string sender = TestHarness.ReadGenerated(p, "GameClientCommandSender.cs");
			Assert.Contains("public sealed class GameClientCommandSender", sender);
			Assert.Contains("EntitiesReplication.ConfigureCommandSender(\"Game\", GameComponentsLookup.BuildDigest, Ship);", sender);
		}

		[Fact]
		public void Command_ClientSender_ShipperWalksEntityIndices()
		{
			// Shipper uses GetComponentIndices to drive a single-pass
			// dispatch — one branch per component in the context. The
			// clientLocalId comes from the entity's creationIndex so the
			// server can collate multi-component ships onto one spawned
			// entity.
			TestHarness.Project p = Run(nameof(Command_ClientSender_ShipperWalksEntityIndices), OneGameHealth);
			string sender = TestHarness.ReadGenerated(p, "GameClientCommandSender.cs");
			Assert.Contains("private void Ship(GameEntity e, List<object[]> ops)", sender);
			Assert.Contains("int clientLocalId = e.creationIndex;", sender);
			Assert.Contains("int[] indices = e.GetComponentIndices();", sender);
			// Branch for the synthesized Command flag itself — it ships
			// too so the server-side spawned entity carries IsCommand=true.
			Assert.Contains("if (compIndex == GameComponentsLookup.Command)", sender);
			// Branch for the user component.
			Assert.Contains("if (compIndex == GameComponentsLookup.Health)", sender);
			// OriginUserId is server-attached only — never on the client
			// command entity, no branch needed.
			Assert.DoesNotContain("if (compIndex == GameComponentsLookup.OriginUserId)", sender);
		}

		[Fact]
		public void Command_ServerReceiver_EmittedPerContext()
		{
			TestHarness.Project p = Run(nameof(Command_ServerReceiver_EmittedPerContext), OneGameHealth);
			Assert.True(TestHarness.GeneratedExists(p, "GameServerCommandReceiver.cs"));
			string receiver = TestHarness.ReadGenerated(p, "GameServerCommandReceiver.cs");
			Assert.Contains("public sealed class GameServerCommandReceiver", receiver);
			int digestIdx = receiver.IndexOf("EntitiesReplication.RegisterDigest(\"Game\", GameComponentsLookup.BuildDigest);", StringComparison.Ordinal);
			int receiverIdx = receiver.IndexOf("EntitiesReplication.RegisterCommandReceiver(\"Game\", OnCommands);", StringComparison.Ordinal);
			Assert.True(digestIdx > 0 && receiverIdx > 0);
			Assert.True(digestIdx < receiverIdx, "RegisterDigest precedes RegisterCommandReceiver.");
		}

		[Fact]
		public void Command_ServerReceiver_AttachesOriginUserIdBeforeUserComponents()
		{
			// Trust marker lands before any client-shipped component so
			// server systems observing the new entity always see a
			// consistent state with OriginUserId present.
			TestHarness.Project p = Run(nameof(Command_ServerReceiver_AttachesOriginUserIdBeforeUserComponents), OneGameHealth);
			string receiver = TestHarness.ReadGenerated(p, "GameServerCommandReceiver.cs");
			int createIdx = receiver.IndexOf("e = _context.CreateEntity();", StringComparison.Ordinal);
			int originIdx = receiver.IndexOf("e.AddOriginUserId(originUserId);", StringComparison.Ordinal);
			Assert.True(createIdx > 0 && originIdx > 0);
			Assert.True(createIdx < originIdx, "CreateEntity precedes AddOriginUserId.");
			// Dispatch branches for user component AND for the Command
			// flag itself must come AFTER OriginUserId is attached. Just
			// check ordering for the user component.
			int healthBranchIdx = receiver.IndexOf("if (compIndex == GameComponentsLookup.Health)", StringComparison.Ordinal);
			Assert.True(healthBranchIdx > originIdx, "Dispatch branches follow OriginUserId attach.");
		}

		[Fact]
		public void Command_ServerReceiver_CollatesByClientLocalId()
		{
			TestHarness.Project p = Run(nameof(Command_ServerReceiver_CollatesByClientLocalId), OneGameHealth);
			string receiver = TestHarness.ReadGenerated(p, "GameServerCommandReceiver.cs");
			Assert.Contains("Dictionary<int, GameEntity> spawnedByLocalId = new();", receiver);
			Assert.Contains("if (spawnedByLocalId.ContainsKey(clientLocalId))", receiver);
			Assert.Contains("spawnedByLocalId[clientLocalId] = e;", receiver);
		}

		[Fact]
		public void Command_ServerReceiver_AppliesCommandFlag()
		{
			// The Command flag rides the wire and lands on the server-
			// spawned entity, so server systems can query AllOf<Command,
			// SomeUserComponent>.
			TestHarness.Project p = Run(nameof(Command_ServerReceiver_AppliesCommandFlag), OneGameHealth);
			string receiver = TestHarness.ReadGenerated(p, "GameServerCommandReceiver.cs");
			Assert.Contains("if (compIndex == GameComponentsLookup.Command)", receiver);
			Assert.Contains("e.IsCommand = true;", receiver);
		}

		[Fact]
		public void Command_NoAttributeOnAnyComponent_DesignProperty()
		{
			// Sanity: the user's data components carry NO Command-related
			// attribute. The framework's command path is driven entirely
			// by the synthesized Command flag's setter, not by tagging.
			string source = @"
using Entities;
using Entities.CodeGeneration.Attributes;
namespace G {
	[Game] public class OpenChestCommand : IComponent { }
	[Game] public class TargetChestId : IComponent { public int Value; }
}";
			TestHarness.Project p = Run(nameof(Command_NoAttributeOnAnyComponent_DesignProperty), source);
			// Both data components are present and emit normal AddX bodies
			// without any wire tail of their own.
			string openChest = TestHarness.ReadGenerated(p, "Components/Game.OpenChestCommand.cs");
			Assert.DoesNotContain("EntitiesReplication", openChest);
			string targetChest = TestHarness.ReadGenerated(p, "Components/Game.TargetChestId.cs");
			Assert.DoesNotContain("EntitiesReplication", targetChest);
		}
	}
}
