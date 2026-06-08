namespace Entities.Tests
{
	// End-to-end pipeline tests â€” drop sample C# in src/, run codegen +
	// transpile, then assert on the rendered Luau. Validates the
	// codegen/runtime contract holds at the Luau emit level (not just
	// at the C# level).
	public class ConversionTests
	{
		private const string Preamble = @"
using Entities;
using Entities.CodeGeneration.Attributes;
";

		private static Dictionary<string, string> Run(string testName, string userSource)
		{
			TestHarness.Project p = TestHarness.Setup(testName);
			TestHarness.WriteSource(p, "User.cs", Preamble + userSource);
			return TestHarness.RunFullPipeline(p);
		}

		// Pulls the rendered user file (src/User.cs â†’ User.luau under the
		// emitted out tree). All conversion tests put user code there.
		private static string GetUser(Dictionary<string, string> outputs)
		{
			foreach (KeyValuePair<string, string> kvp in outputs)
				if (kvp.Key.EndsWith("User.luau", StringComparison.OrdinalIgnoreCase))
					return kvp.Value;
			throw new InvalidOperationException(
				"No User.luau in outputs. Got: " + string.Join(", ", outputs.Keys));
		}

		private static string GetEntity(Dictionary<string, string> outputs)
			=> outputs.FirstOrDefault(kv => kv.Key.EndsWith("Generated/GameEntity.luau",
				StringComparison.OrdinalIgnoreCase)).Value
				?? throw new InvalidOperationException("Missing GameEntity.luau");

		private static string GetMatcher(Dictionary<string, string> outputs)
			=> outputs.FirstOrDefault(kv => kv.Key.EndsWith("Generated/GameMatcher.luau",
				StringComparison.OrdinalIgnoreCase)).Value
				?? throw new InvalidOperationException("Missing GameMatcher.luau");

		private static string GetContext(Dictionary<string, string> outputs)
			=> outputs.FirstOrDefault(kv => kv.Key.EndsWith("Generated/GameContext.luau",
				StringComparison.OrdinalIgnoreCase)).Value
				?? throw new InvalidOperationException("Missing GameContext.luau");

		// ----------------------------------------------------------------
		// Flag component â†’ IsX get/set on GameEntity.
		// ----------------------------------------------------------------

		[Fact]
		public void Flag_IsXGetter_LowersToHasComponentCall()
		{
			Dictionary<string, string> outputs = Run(
				nameof(Flag_IsXGetter_LowersToHasComponentCall),
				@"namespace U { [Game] public class Player : IComponent { } }");

			string entity = GetEntity(outputs);
			Assert.Contains("function GameEntity:IsPlayer()", entity);
			Assert.Contains("HasComponent(GameComponentsLookup.Player)", entity);
		}

		[Fact]
		public void Flag_IsXSetter_LowersToAddOrRemoveComponent()
		{
			Dictionary<string, string> outputs = Run(
				nameof(Flag_IsXSetter_LowersToAddOrRemoveComponent),
				@"namespace U { [Game] public class Player : IComponent { } }");

			string entity = GetEntity(outputs);
			Assert.Contains("function GameEntity:set_IsPlayer(value)", entity);
			Assert.Contains("AddComponent(GameComponentsLookup.Player", entity);
			Assert.Contains("RemoveComponent(GameComponentsLookup.Player)", entity);
		}

		[Fact]
		public void Flag_UserSourceAssignment_LowersToSetterMethodCall()
		{
			Dictionary<string, string> outputs = Run(
				nameof(Flag_UserSourceAssignment_LowersToSetterMethodCall),
				@"namespace U {
					[Game] public class Player : IComponent { }
					public class Bootstrap {
						public void Start(GameContext ctx) {
							GameEntity e = ctx.CreateEntity();
							e.IsPlayer = true;
						}
					}
				}");

			string user = GetUser(outputs);
			// `e.IsPlayer = true` should lower to a colon-style setter call.
			Assert.Contains("e:set_IsPlayer(true)", user);
		}

		// ----------------------------------------------------------------
		// Value component â†’ property + setter (single Value field).
		// ----------------------------------------------------------------

		[Fact]
		public void Value_GetterUnwrapsValueField()
		{
			Dictionary<string, string> outputs = Run(
				nameof(Value_GetterUnwrapsValueField),
				@"namespace U { [Game] public class Health : IComponent { public int Value; } }");

			string entity = GetEntity(outputs);
			Assert.Contains("function GameEntity:Health()", entity);
			Assert.Contains("(self:GetComponent(GameComponentsLookup.Health)).Value", entity);
		}

		[Fact]
		public void Value_SetterReplacesComponent()
		{
			Dictionary<string, string> outputs = Run(
				nameof(Value_SetterReplacesComponent),
				@"namespace U { [Game] public class Health : IComponent { public int Value; } }");

			string entity = GetEntity(outputs);
			Assert.Contains("function GameEntity:set_Health(value)", entity);
			Assert.Contains("self:ReplaceComponent(GameComponentsLookup.Health", entity);
		}

		[Fact]
		public void Value_UserSourceAssignment_LowersToSetterMethodCall()
		{
			Dictionary<string, string> outputs = Run(
				nameof(Value_UserSourceAssignment_LowersToSetterMethodCall),
				@"namespace U {
					[Game] public class Health : IComponent { public int Value; }
					public class Bootstrap {
						public void Tick(GameEntity e) { e.Health = 7; }
					}
				}");

			string user = GetUser(outputs);
			Assert.Contains("e:set_Health(7)", user);
		}

		[Fact]
		public void Value_UserSourceRead_LowersToGetterMethodCall()
		{
			Dictionary<string, string> outputs = Run(
				nameof(Value_UserSourceRead_LowersToGetterMethodCall),
				@"namespace U {
					[Game] public class Health : IComponent { public int Value; }
					public class Bootstrap {
						public int Read(GameEntity e) { return e.Health; }
					}
				}");

			string user = GetUser(outputs);
			Assert.Contains("e:Health()", user);
		}

		[Fact]
		public void Value_AddX_LowersToAddComponent()
		{
			Dictionary<string, string> outputs = Run(
				nameof(Value_AddX_LowersToAddComponent),
				@"namespace U { [Game] public class Health : IComponent { public int Value; } }");

			string entity = GetEntity(outputs);
			Assert.Contains("function GameEntity:AddHealth(newValue)", entity);
			Assert.Contains("self:AddComponent(GameComponentsLookup.Health", entity);
		}

		[Fact]
		public void Value_ReplaceX_LowersToReplaceComponent()
		{
			Dictionary<string, string> outputs = Run(
				nameof(Value_ReplaceX_LowersToReplaceComponent),
				@"namespace U { [Game] public class Health : IComponent { public int Value; } }");

			string entity = GetEntity(outputs);
			Assert.Contains("function GameEntity:ReplaceHealth(newValue)", entity);
			Assert.Contains("self:ReplaceComponent(GameComponentsLookup.Health", entity);
		}

		[Fact]
		public void Value_RemoveX_LowersToRemoveComponent()
		{
			Dictionary<string, string> outputs = Run(
				nameof(Value_RemoveX_LowersToRemoveComponent),
				@"namespace U { [Game] public class Health : IComponent { public int Value; } }");

			string entity = GetEntity(outputs);
			Assert.Contains("function GameEntity:RemoveHealth()", entity);
			Assert.Contains("self:RemoveComponent(GameComponentsLookup.Health)", entity);
		}

		[Fact]
		public void Value_HasX_LowersToHasComponent()
		{
			Dictionary<string, string> outputs = Run(
				nameof(Value_HasX_LowersToHasComponent),
				@"namespace U { [Game] public class Health : IComponent { public int Value; } }");

			string entity = GetEntity(outputs);
			Assert.Contains("function GameEntity:HasHealth()", entity);
			Assert.Contains("self:HasComponent(GameComponentsLookup.Health)", entity);
		}

		// ----------------------------------------------------------------
		// Multi-field component â€” read returns the component instance.
		// ----------------------------------------------------------------

		[Fact]
		public void MultiField_GetterReturnsComponentInstance()
		{
			Dictionary<string, string> outputs = Run(
				nameof(MultiField_GetterReturnsComponentInstance),
				@"namespace U { [Game] public class Pos : IComponent { public int X; public int Y; } }");

			string entity = GetEntity(outputs);
			Assert.Contains("function GameEntity:Pos()", entity);
			Assert.Contains("self:GetComponent(GameComponentsLookup.Pos)", entity);
			Assert.DoesNotContain(".Value", entity);
			// No setter â€” the user must call ReplacePos(x, y).
			Assert.DoesNotContain("set_Pos", entity);
		}

		[Fact]
		public void MultiField_AddX_PassesAllFieldsAsArgs()
		{
			Dictionary<string, string> outputs = Run(
				nameof(MultiField_AddX_PassesAllFieldsAsArgs),
				@"namespace U { [Game] public class Pos : IComponent { public int X; public int Y; } }");

			string entity = GetEntity(outputs);
			Assert.Contains("function GameEntity:AddPos(newX, newY)", entity);
			Assert.Contains("component.X = newX", entity);
			Assert.Contains("component.Y = newY", entity);
		}

		// ----------------------------------------------------------------
		// Matcher â€” fluent builder, params wrap.
		// ----------------------------------------------------------------

		[Fact]
		public void Matcher_PerComponentStaticGetter_LowersToZeroArgFunction()
		{
			Dictionary<string, string> outputs = Run(
				nameof(Matcher_PerComponentStaticGetter_LowersToZeroArgFunction),
				@"namespace U { [Game] public class Health : IComponent { public int Value; } }");

			string matcher = GetMatcher(outputs);
			Assert.Contains("function GameMatcher.Health()", matcher);
		}

		[Fact]
		public void Matcher_AllOf_WithMultipleMatchers_WrapsArgsInTable()
		{
			// The whole point of the `params` fix â€” multi-arg AllOf must
			// land as a single Lua table.
			Dictionary<string, string> outputs = Run(
				nameof(Matcher_AllOf_WithMultipleMatchers_WrapsArgsInTable),
				@"namespace U {
					[Game] public class A : IComponent { }
					[Game] public class B : IComponent { }
					public class Sys : IExecuteSystem {
						private readonly IGroup<GameEntity> _g;
						public Sys(GameContext ctx) { _g = ctx.GetGroup(GameMatcher.AllOf(GameMatcher.A, GameMatcher.B)); }
						public void Execute() { }
					}
				}");

			string user = GetUser(outputs);
			Assert.Contains("GameMatcher.AllOf({GameMatcher:A(), GameMatcher:B()})", user);
		}

		[Fact]
		public void Matcher_AllOf_WithChainedNoneOf_PreservesChain()
		{
			Dictionary<string, string> outputs = Run(
				nameof(Matcher_AllOf_WithChainedNoneOf_PreservesChain),
				@"namespace U {
					[Game] public class A : IComponent { }
					[Game] public class B : IComponent { }
					public class Sys : IExecuteSystem {
						private readonly IGroup<GameEntity> _g;
						public Sys(GameContext ctx) {
							_g = ctx.GetGroup(GameMatcher.AllOf(GameMatcher.A).NoneOf(GameMatcher.B));
						}
						public void Execute() { }
					}
				}");

			string user = GetUser(outputs);
			Assert.Contains(":NoneOf({GameMatcher:B()})", user);
		}

		[Fact]
		public void Matcher_DefinedOncePerContext_CallsAllOfIndicesWithSingleIndex()
		{
			// GameMatcher.Player's body calls Matcher.AllOfIndices<T>(Player_index).
			// The transpiler wraps the single `params int[]` arg into {idx}.
			Dictionary<string, string> outputs = Run(
				nameof(Matcher_DefinedOncePerContext_CallsAllOfIndicesWithSingleIndex),
				@"namespace U { [Game] public class Player : IComponent { } }");

			string matcher = GetMatcher(outputs);
			Assert.Contains("Matcher.AllOfIndices", matcher);
			Assert.Contains("{GameComponentsLookup.Player}", matcher);
		}

		// ----------------------------------------------------------------
		// Context â€” CreateEntity / GetGroup chains.
		// ----------------------------------------------------------------

		[Fact]
		public void Context_CreateEntityInstance_OverrideEmits()
		{
			Dictionary<string, string> outputs = Run(
				nameof(Context_CreateEntityInstance_OverrideEmits),
				@"namespace U { [Game] public class Player : IComponent { } }");

			string ctx = GetContext(outputs);
			Assert.Contains("function GameContext:CreateEntityInstance()", ctx);
			Assert.Contains("GameEntity.new()", ctx);
		}

		[Fact]
		public void Context_CreateEntity_FromUserCode_LowersToColonCall()
		{
			Dictionary<string, string> outputs = Run(
				nameof(Context_CreateEntity_FromUserCode_LowersToColonCall),
				@"namespace U {
					[Game] public class Player : IComponent { }
					public class Bootstrap {
						public void Make(GameContext ctx) {
							GameEntity e = ctx.CreateEntity();
						}
					}
				}");

			string user = GetUser(outputs);
			Assert.Contains("ctx:CreateEntity()", user);
		}

		[Fact]
		public void Context_GetGroup_FromUserCode_LowersToColonCall()
		{
			Dictionary<string, string> outputs = Run(
				nameof(Context_GetGroup_FromUserCode_LowersToColonCall),
				@"namespace U {
					[Game] public class Player : IComponent { }
					public class Sys : IExecuteSystem {
						private readonly IGroup<GameEntity> _g;
						public Sys(GameContext ctx) { _g = ctx.GetGroup(GameMatcher.Player); }
						public void Execute() { }
					}
				}");

			string user = GetUser(outputs);
			Assert.Contains("ctx:GetGroup(", user);
		}

		// ----------------------------------------------------------------
		// Systems lifecycle â€” IExecuteSystem fields/methods emit correctly.
		// ----------------------------------------------------------------

		[Fact]
		public void System_ExecuteMethod_LowersToColonMethod()
		{
			Dictionary<string, string> outputs = Run(
				nameof(System_ExecuteMethod_LowersToColonMethod),
				@"namespace U {
					public class TickSys : IExecuteSystem {
						public void Execute() { }
					}
				}");

			string user = GetUser(outputs);
			Assert.Contains("function TickSys:Execute()", user);
		}

		[Fact]
		public void Systems_AddSystem_LowersToColonAdd()
		{
			Dictionary<string, string> outputs = Run(
				nameof(Systems_AddSystem_LowersToColonAdd),
				@"namespace U {
					public class TickSys : IExecuteSystem { public void Execute() { } }
					public class Bootstrap {
						public void Wire() {
							Systems systems = new Systems();
							systems.Add(new TickSys());
						}
					}
				}");

			string user = GetUser(outputs);
			Assert.Contains("systems:Add(TickSys.new())", user);
		}

		[Fact]
		public void System_ConstructorInjection_PreservesContextDependency()
		{
			Dictionary<string, string> outputs = Run(
				nameof(System_ConstructorInjection_PreservesContextDependency),
				@"namespace U {
					[Game] public class Player : IComponent { }
					public class Sys : IExecuteSystem {
						private readonly GameContext _ctx;
						public Sys(GameContext ctx) { _ctx = ctx; }
						public void Execute() { }
					}
				}");

			string user = GetUser(outputs);
			Assert.Contains("Sys.__ctorParams = {GameContext}", user);
			Assert.Contains("function Sys:constructor(ctx)", user);
		}

		// ----------------------------------------------------------------
		// Realistic snippet: the demo's DamagePlayerSystem.
		// ----------------------------------------------------------------

		[Fact]
		public void EndToEnd_DamagePlayerSystem_RoundTripsToExpectedLua()
		{
			// Smoke-tests the whole shape: matcher chain, Execute body,
			// the value-property read on the entity, the setter back.
			Dictionary<string, string> outputs = Run(
				nameof(EndToEnd_DamagePlayerSystem_RoundTripsToExpectedLua),
				@"namespace U {
					[Game] public class Player : IComponent { }
					[Game] public class Health : IComponent { public int Value; }
					public class DamagePlayerSystem : IExecuteSystem {
						private readonly IGroup<GameEntity> _players;
						public DamagePlayerSystem(GameContext ctx) {
							_players = ctx.GetGroup(GameMatcher.AllOf(GameMatcher.Player, GameMatcher.Health));
						}
						public void Execute() {
							foreach (GameEntity e in _players.GetEntities()) {
								e.Health = e.Health - 1;
							}
						}
					}
				}");

			string user = GetUser(outputs);

			// Constructor wiring â€” matchers wrap into a table.
			Assert.Contains("GameMatcher.AllOf({GameMatcher:Player(), GameMatcher:Health()})", user);
			// Execute body â€” read uses getter, write uses setter.
			Assert.Contains("e:set_Health(e:Health() - 1)", user);
		}

		// ----------------------------------------------------------------
		// IExecuteSystem interface routing â€” `:IExecuteSystem` in C#
		// should put "IExecuteSystem" on the Lua-side __interfaces table.
		// ----------------------------------------------------------------

		[Fact]
		public void System_InterfaceTag_AppearsOnInterfacesTable()
		{
			Dictionary<string, string> outputs = Run(
				nameof(System_InterfaceTag_AppearsOnInterfacesTable),
				@"namespace U {
					public class Sys : IExecuteSystem { public void Execute() { } }
				}");

			string user = GetUser(outputs);
			Assert.Contains("Sys.__interfaces = {\"IExecuteSystem\"}", user);
		}

		[Fact]
		public void Component_InterfaceTag_AppearsOnInterfacesTable()
		{
			Dictionary<string, string> outputs = Run(
				nameof(Component_InterfaceTag_AppearsOnInterfacesTable),
				@"namespace U { [Game] public class Health : IComponent { public int Value; } }");

			string user = GetUser(outputs);
			Assert.Contains("Health.__interfaces = {\"IComponent\"}", user);
		}

		// ----------------------------------------------------------------
		// Plugin imports â€” references to runtime classes resolve to the
		// ReplicatedStorage.Plugins.Entities path.
		// ----------------------------------------------------------------

		[Fact]
		public void GameContext_ImportsContextFromPluginRuntime()
		{
			Dictionary<string, string> outputs = Run(
				nameof(GameContext_ImportsContextFromPluginRuntime),
				@"namespace U { [Game] public class Player : IComponent { } }");

			string ctx = GetContext(outputs);
			Assert.Contains("\"Plugins\", \"Entities\", \"Context\"", ctx);
		}

		[Fact]
		public void GameEntity_ImportsEntityFromPluginRuntime()
		{
			Dictionary<string, string> outputs = Run(
				nameof(GameEntity_ImportsEntityFromPluginRuntime),
				@"namespace U { [Game] public class Player : IComponent { } }");

			string entity = GetEntity(outputs);
			Assert.Contains("\"Plugins\", \"Entities\", \"Entity\"", entity);
		}

		[Fact]
		public void GameMatcher_ImportsMatcherFromPluginRuntime()
		{
			Dictionary<string, string> outputs = Run(
				nameof(GameMatcher_ImportsMatcherFromPluginRuntime),
				@"namespace U { [Game] public class Player : IComponent { } }");

			string matcher = GetMatcher(outputs);
			Assert.Contains("\"Plugins\", \"Entities\", \"Matcher\"", matcher);
		}

		// ----------------------------------------------------------------
		// Regression: AnyOf used in user code must transpile end-to-end.
		// Before the fix, the non-generic Matcher.AnyOf<T> helper routed
		// through Matcher<T>.AnyOf (instance), causing a C# compile error
		// on the generated GameMatcher.AnyOf body.
		// ----------------------------------------------------------------

		[Fact]
		public void Matcher_AnyOf_FromUserCode_RoundTripsToWrappedTable()
		{
			Dictionary<string, string> outputs = Run(
				nameof(Matcher_AnyOf_FromUserCode_RoundTripsToWrappedTable),
				@"namespace U {
					[Game] public class A : IComponent { }
					[Game] public class B : IComponent { }
					public class Sys : IExecuteSystem {
						private readonly IGroup<GameEntity> _g;
						public Sys(GameContext ctx) {
							_g = ctx.GetGroup(GameMatcher.AnyOf(GameMatcher.A, GameMatcher.B));
						}
						public void Execute() { }
					}
				}");

			string user = GetUser(outputs);
			Assert.Contains("GameMatcher.AnyOf({GameMatcher:A(), GameMatcher:B()})", user);
		}

		[Fact]
		public void Matcher_GeneratedAnyOfBody_RoutesThroughNonGenericMatcher()
		{
			// Locks the GameMatcher.AnyOf body so it stays routed through
			// the non-generic helper — the original code path called
			// Matcher<T>.AnyOf which is instance-only.
			Dictionary<string, string> outputs = Run(
				nameof(Matcher_GeneratedAnyOfBody_RoutesThroughNonGenericMatcher),
				@"namespace U { [Game] public class Player : IComponent { } }");

			string matcher = GetMatcher(outputs);
			// Either dot or colon — the static getter emits as a zero-arg
			// function call, but the AnyOf body must hit the runtime's
			// static Matcher.AnyOf (which takes the leading erased type
			// arg, hence the GameEntity import as first arg).
			Assert.Contains("Matcher.AnyOf(", matcher);
		}

		// ----------------------------------------------------------------
		// Regression: Initialize on IEntity / Context.Initialize helper.
		// ----------------------------------------------------------------

		[Fact]
		public void Entity_Initialize_CanBeCalledThroughIEntityInterface()
		{
			// If IEntity didn't declare Initialize, this test source would
			// fail C# compile because `e.Initialize(...)` on an IEntity
			// reference would not resolve. Running the pipeline at all
			// proves the interface carries the method.
			Dictionary<string, string> outputs = Run(
				nameof(Entity_Initialize_CanBeCalledThroughIEntityInterface),
				@"namespace U {
					public class Setup {
						public void Wire(IEntity e) { e.Initialize(0, 1); }
					}
				}");

			string user = GetUser(outputs);
			Assert.Contains("e:Initialize(0, 1)", user);
		}

		[Fact]
		public void Context_Initialize_HelperRoutesEntityWiringThroughCreateEntity()
		{
			// The Context.Initialize helper is what CreateEntity goes
			// through internally — exposed publicly so tests / framework
			// code can wire a pre-built entity without going through
			// CreateEntity. Asserts the user-facing call lowers cleanly.
			Dictionary<string, string> outputs = Run(
				nameof(Context_Initialize_HelperRoutesEntityWiringThroughCreateEntity),
				@"namespace U {
					[Game] public class Player : IComponent { }
					public class Setup {
						public void Mount(GameContext ctx, GameEntity e) { ctx.Initialize(e); }
					}
				}");

			string user = GetUser(outputs);
			Assert.Contains("ctx:Initialize(e)", user);
		}

		// ----------------------------------------------------------------
		// Entity pool — Destroy recycles into Context._reusableEntities,
		// next CreateEntity pops + Reactivates instead of allocating fresh.
		// Tests at this level only assert the API surface lowers — runtime
		// behaviour is verified by reading the hand-written Luau directly.
		// ----------------------------------------------------------------

		[Fact]
		public void Entity_Reactivate_CanBeCalledThroughIEntityInterface()
		{
			Dictionary<string, string> outputs = Run(
				nameof(Entity_Reactivate_CanBeCalledThroughIEntityInterface),
				@"namespace U {
					public class Setup {
						public void Wire(IEntity e) { e.Reactivate(7); }
					}
				}");

			string user = GetUser(outputs);
			Assert.Contains("e:Reactivate(7)", user);
		}

		[Fact]
		public void Context_ReusableEntitiesCount_AccessibleThroughIContextInterface()
		{
			Dictionary<string, string> outputs = Run(
				nameof(Context_ReusableEntitiesCount_AccessibleThroughIContextInterface),
				@"namespace U {
					public class Telemetry {
						public int Pooled(IContext ctx) { return ctx.reusableEntitiesCount; }
					}
				}");

			string user = GetUser(outputs);
			// Property access on instance lowers to a getter-method call.
			Assert.Contains("ctx:reusableEntitiesCount()", user);
		}

		[Fact]
		public void Entity_Destroy_LowersToColonMethodCall()
		{
			Dictionary<string, string> outputs = Run(
				nameof(Entity_Destroy_LowersToColonMethodCall),
				@"namespace U {
					[Game] public class Player : IComponent { }
					public class Setup {
						public void Kill(GameEntity e) { e.Destroy(); }
					}
				}");

			string user = GetUser(outputs);
			// In Lua, Destroy fires the back-channel to Context for the
			// reuse-stack push. The lowering itself is just the method call.
			Assert.Contains("e:Destroy()", user);
		}

		// ----------------------------------------------------------------
		// Component pool — Add/Replace generated bodies route through
		// CreateComponent<T> in the emitted Luau, and ClearComponentPool
		// helpers are reachable on Context.
		// ----------------------------------------------------------------

		[Fact]
		public void Entity_AddX_GeneratedBody_LowersToCreateComponentCall()
		{
			Dictionary<string, string> outputs = Run(
				nameof(Entity_AddX_GeneratedBody_LowersToCreateComponentCall),
				@"namespace U { [Game] public class Health : IComponent { public int Value; } }");

			string entity = GetEntity(outputs);
			// The Luau body should call CreateComponent with the leading
			// erased type-arg (Health import) and the lookup index.
			Assert.Contains(":CreateComponent(", entity);
		}

		[Fact]
		public void Entity_ReplaceX_GeneratedBody_LowersToCreateComponentCall()
		{
			Dictionary<string, string> outputs = Run(
				nameof(Entity_ReplaceX_GeneratedBody_LowersToCreateComponentCall),
				@"namespace U { [Game] public class Health : IComponent { public int Value; } }");

			string entity = GetEntity(outputs);
			// Both Add and Replace funnel through CreateComponent.
			int hits = System.Text.RegularExpressions.Regex
				.Matches(entity, @":CreateComponent\(").Count;
			Assert.True(hits >= 2, $"Expected CreateComponent in Add and Replace, got {hits}.");
		}

		[Fact]
		public void Entity_CreateComponent_FromUserCode_PassesTypeArgFirst()
		{
			// User-side `e.CreateComponent<Health>(0)` lowers with the
			// type-arg as the leading runtime arg per the converter's
			// generic-erasure convention (commit 3613bf0).
			Dictionary<string, string> outputs = Run(
				nameof(Entity_CreateComponent_FromUserCode_PassesTypeArgFirst),
				@"namespace U {
					[Game] public class Health : IComponent { public int Value; }
					public class Setup {
						public void Make(GameEntity e) {
							Health h = e.CreateComponent<Health>(0);
						}
					}
				}");

			string user = GetUser(outputs);
			Assert.Contains("e:CreateComponent(Health, 0)", user);
		}

		[Fact]
		public void Context_ClearComponentPool_LowersToColonCall()
		{
			Dictionary<string, string> outputs = Run(
				nameof(Context_ClearComponentPool_LowersToColonCall),
				@"namespace U {
					[Game] public class Player : IComponent { }
					public class Setup {
						public void OnSceneEnd(GameContext ctx) { ctx.ClearComponentPool(0); }
					}
				}");

			string user = GetUser(outputs);
			Assert.Contains("ctx:ClearComponentPool(0)", user);
		}

		[Fact]
		public void Context_ClearComponentPools_LowersToColonCall()
		{
			Dictionary<string, string> outputs = Run(
				nameof(Context_ClearComponentPools_LowersToColonCall),
				@"namespace U {
					[Game] public class Player : IComponent { }
					public class Setup {
						public void OnSceneEnd(GameContext ctx) { ctx.ClearComponentPools(); }
					}
				}");

			string user = GetUser(outputs);
			Assert.Contains("ctx:ClearComponentPools()", user);
		}

		// ----------------------------------------------------------------
		// Direct foreach on IGroup<T>. IGroup<T> inherits IEnumerable<T>;
		// runtime/Group.luau's __iter metamethod makes the Luau-side
		// generalized-for over a Group iterate its entities.
		// ----------------------------------------------------------------

		[Fact]
		public void Group_DirectForeach_LowersToGeneralizedFor()
		{
			Dictionary<string, string> outputs = Run(
				nameof(Group_DirectForeach_LowersToGeneralizedFor),
				@"namespace U {
					[Game] public class Player : IComponent { }
					public class Sys : IExecuteSystem {
						private readonly IGroup<GameEntity> _g;
						public Sys(GameContext ctx) { _g = ctx.GetGroup(GameMatcher.Player); }
						public void Execute() {
							foreach (var e in _g) { }
						}
					}
				}");

			string user = GetUser(outputs);
			// The generalized-for shape (no `:GetEntities()` call) is what
			// triggers Luau's __iter metamethod lookup on the group's metatable.
			Assert.Contains("for _, e in self._g do", user);
		}
	}
}

