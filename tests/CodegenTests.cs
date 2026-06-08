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
			Assert.Contains("public const int Real = 0;", lookup);
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
			Assert.Contains("public const int Apple = 0;", lookup);
			Assert.Contains("public const int Mango = 1;", lookup);
			Assert.Contains("public const int Zebra = 2;", lookup);
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
		public void ClientReplication_ConstructorRegistersSchemasAppliersAndRemovers()
		{
			// Schema + per-component Apply / Remove registration + the one
			// SubscribeClient call that wires OnClientEvent and fires the
			// Ready ping. Runtime does all buffer unpack; ClientReplication
			// just exposes typed callbacks.
			TestHarness.Project p = Run(nameof(ClientReplication_ConstructorRegistersSchemasAppliersAndRemovers), OneReplicatedHealth);
			string mirror = TestHarness.ReadGenerated(p, "GameClientReplication.cs");
			Assert.Contains("EntitiesReplication.RegisterComponentSchema(\"Game\", GameComponentsLookup.Health, new int[] { EntitiesFieldType.I32 });", mirror);
			Assert.Contains("EntitiesReplication.RegisterClientApplier(\"Game\", GameComponentsLookup.Health, ApplyHealth);", mirror);
			Assert.Contains("EntitiesReplication.RegisterClientRemover(\"Game\", GameComponentsLookup.Health, RemoveHealth);", mirror);
			Assert.Contains("EntitiesReplication.SubscribeClient(\"Game\");", mirror);
			Assert.DoesNotContain("OnOps", mirror);
			Assert.DoesNotContain("ApplySet", mirror);
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
		public void ClientReplication_ApplyMethod_Value_HasTypedFieldParameter()
		{
			// Per-component Apply{X}(int serverId, T1 v1, ...) — runtime
			// unpacks the buffer and calls with typed args. Apply picks
			// Replace vs Add by HasX for idempotency.
			TestHarness.Project p = Run(nameof(ClientReplication_ApplyMethod_Value_HasTypedFieldParameter), OneReplicatedHealth);
			string mirror = TestHarness.ReadGenerated(p, "GameClientReplication.cs");
			Assert.Contains("private void ApplyHealth(int serverId, int newValue)", mirror);
			Assert.Contains("GameEntity e = GetOrCreate(serverId);", mirror);
			Assert.Contains("if (e.HasHealth) e.ReplaceHealth(newValue);", mirror);
			Assert.Contains("else e.AddHealth(newValue);", mirror);
		}

		[Fact]
		public void ClientReplication_RemoveMethod_Value_TakesOnlyServerId()
		{
			TestHarness.Project p = Run(nameof(ClientReplication_RemoveMethod_Value_TakesOnlyServerId), OneReplicatedHealth);
			string mirror = TestHarness.ReadGenerated(p, "GameClientReplication.cs");
			Assert.Contains("private void RemoveHealth(int serverId)", mirror);
			Assert.Contains("if (_byServerId.ContainsKey(serverId)) _byServerId[serverId].RemoveHealth();", mirror);
		}

		[Fact]
		public void ClientReplication_ApplyMethod_Flag_TakesOnlyServerId()
		{
			TestHarness.Project p = Run(nameof(ClientReplication_ApplyMethod_Flag_TakesOnlyServerId), OneReplicatedFlag);
			string mirror = TestHarness.ReadGenerated(p, "GameClientReplication.cs");
			Assert.Contains("private void ApplyStunned(int serverId)", mirror);
			Assert.Contains("GetOrCreate(serverId).IsStunned = true;", mirror);
		}

		[Fact]
		public void ClientReplication_RemoveMethod_Flag_SetsIsXFalse()
		{
			TestHarness.Project p = Run(nameof(ClientReplication_RemoveMethod_Flag_SetsIsXFalse), OneReplicatedFlag);
			string mirror = TestHarness.ReadGenerated(p, "GameClientReplication.cs");
			Assert.Contains("private void RemoveStunned(int serverId)", mirror);
			Assert.Contains("if (_byServerId.ContainsKey(serverId)) _byServerId[serverId].IsStunned = false;", mirror);
		}

		[Fact]
		public void ClientReplication_SchemaArray_MultiField_HasOneEntryPerField()
		{
			// Multi-field components register a schema with field types in
			// declaration order so the runtime knows the unpack layout.
			string source = @"
using Entities;
using Entities.CodeGeneration.Attributes;
namespace G { [Game, Replicated] public class Pos : IComponent { public float X; public float Y; } }";
			TestHarness.Project p = Run(nameof(ClientReplication_SchemaArray_MultiField_HasOneEntryPerField), source);
			string mirror = TestHarness.ReadGenerated(p, "GameClientReplication.cs");
			Assert.Contains("RegisterComponentSchema(\"Game\", GameComponentsLookup.Pos, new int[] { EntitiesFieldType.F32, EntitiesFieldType.F32 });", mirror);
			Assert.Contains("private void ApplyPos(int serverId, float newX, float newY)", mirror);
			Assert.Contains("if (e.HasPos) e.ReplacePos(newX, newY);", mirror);
		}

		[Fact]
		public void ClientReplication_SchemaArray_Flag_IsEmpty()
		{
			TestHarness.Project p = Run(nameof(ClientReplication_SchemaArray_Flag_IsEmpty), OneReplicatedFlag);
			string mirror = TestHarness.ReadGenerated(p, "GameClientReplication.cs");
			Assert.Contains("RegisterComponentSchema(\"Game\", GameComponentsLookup.Stunned, new int[0]);", mirror);
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
		public void ServerReplication_ConstructorRegistersSchemasAndSnapshotter()
		{
			// Schema registration + snapshotter registration. Snapshotter
			// is the fn the runtime invokes per Ready ping; schemas tell
			// the runtime how to pack the QueueSet calls into the buffer.
			TestHarness.Project p = Run(nameof(ServerReplication_ConstructorRegistersSchemasAndSnapshotter), OneReplicatedHealth);
			string server = TestHarness.ReadGenerated(p, "GameServerReplication.cs");
			Assert.Contains("public sealed class GameServerReplication", server);
			Assert.Contains("private readonly GameContext _context;", server);
			Assert.Contains("EntitiesReplication.RegisterComponentSchema(\"Game\", GameComponentsLookup.Health, new int[] { EntitiesFieldType.I32 });", server);
			Assert.Contains("EntitiesReplication.RegisterSnapshotter(\"Game\", Snapshot);", server);
		}

		[Fact]
		public void ServerReplication_Snapshot_IsVoidAndUsesQueueSet()
		{
			// New Snapshot returns void — the runtime sets _snapshotMode so
			// every QueueSet inside lands in the per-player snap buffer.
			// No more List<object> / ops.ToArray() boxing.
			TestHarness.Project p = Run(nameof(ServerReplication_Snapshot_IsVoidAndUsesQueueSet), OneReplicatedHealth);
			string server = TestHarness.ReadGenerated(p, "GameServerReplication.cs");
			Assert.Contains("private void Snapshot()", server);
			Assert.Contains("GameEntity[] entities = _context.GetEntities();", server);
			Assert.Contains("if (e.HasHealth)", server);
			Assert.Contains("EntitiesReplication.QueueSet(\"Game\", GameComponentsLookup.Health, e.creationIndex, e.Health);", server);
			Assert.DoesNotContain("List<object>", server);
			Assert.DoesNotContain("ops.ToArray", server);
		}

		[Fact]
		public void ServerReplication_Snapshot_FlagComponent_EmitsBareQueueSet()
		{
			TestHarness.Project p = Run(nameof(ServerReplication_Snapshot_FlagComponent_EmitsBareQueueSet), OneReplicatedFlag);
			string server = TestHarness.ReadGenerated(p, "GameServerReplication.cs");
			Assert.Contains("if (e.IsStunned) EntitiesReplication.QueueSet(\"Game\", GameComponentsLookup.Stunned, e.creationIndex);", server);
		}

		[Fact]
		public void ServerReplication_Snapshot_MultiFieldComponent_UnpacksThenQueues()
		{
			// Multi-field still pulls the component instance and unpacks
			// each field positionally into the QueueSet varargs.
			string source = @"
using Entities;
using Entities.CodeGeneration.Attributes;
namespace G { [Game, Replicated] public class Pos : IComponent { public int X; public int Y; } }";
			TestHarness.Project p = Run(nameof(ServerReplication_Snapshot_MultiFieldComponent_UnpacksThenQueues), source);
			string server = TestHarness.ReadGenerated(p, "GameServerReplication.cs");
			Assert.Contains("if (e.HasPos)", server);
			Assert.Contains("global::G.Pos comp = e.Pos;", server);
			Assert.Contains("EntitiesReplication.QueueSet(\"Game\", GameComponentsLookup.Pos, e.creationIndex, comp.X, comp.Y);", server);
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

		[Fact]
		public void ServerReplication_SchemaArray_MultiField_ListsTypesInOrder()
		{
			string source = @"
using Entities;
using Entities.CodeGeneration.Attributes;
namespace G { [Game, Replicated] public class Pos : IComponent { public float X; public float Y; } }";
			TestHarness.Project p = Run(nameof(ServerReplication_SchemaArray_MultiField_ListsTypesInOrder), source);
			string server = TestHarness.ReadGenerated(p, "GameServerReplication.cs");
			Assert.Contains("RegisterComponentSchema(\"Game\", GameComponentsLookup.Pos, new int[] { EntitiesFieldType.F32, EntitiesFieldType.F32 });", server);
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
			// one and throws on mismatch.
			TestHarness.Project p = Run(nameof(Unique_Flag_HooksThrowOnConflictingEntity), OneUniqueFlag);
			string comp = TestHarness.ReadGenerated(p, "Components/Game.GameSession.cs");
			Assert.Contains("internal void _SetGameSessionEntity(GameEntity entity)", comp);
			Assert.Contains("throw new System.Exception(\"Unique component GameSession is already assigned to a different entity.\");", comp);
			Assert.Contains("internal void _ClearGameSessionEntity()", comp);
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
			Assert.Contains("internal void _RegisterUser(GameEntity entity, string key)", comp);
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
			TestHarness.Project p = Run(nameof(EntityIndex_NonPrimary_RegisterAppendsToSet), OneIndexedOwned);
			string comp = TestHarness.ReadGenerated(p, "Components/Game.Owned.cs");
			Assert.Contains("internal void _RegisterOwned(GameEntity entity, int key)", comp);
			Assert.Contains("set.Add(entity);", comp);
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
	}
}
