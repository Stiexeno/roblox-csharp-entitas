# roblox-csharp-entities

ECS for Roblox, packaged as a [roblox-csharp](https://github.com/Stiexeno/roblox-csharp) plugin. Write components and systems with the familiar `[Game] class Health : IComponent`, `class FooSystem : IExecuteSystem`, `game.GetGroup(GameMatcher.AllOf(...).NoneOf(...))` shape. The plugin's codegen runs on every `roblox-csharp dev` tick and writes `Generated/Contexts.cs` / `GameContext.cs` / `GameEntity.cs` / `GameMatcher.cs` / `GameComponentsLookup.cs` partials into `src/Generated/`, so your IDE sees `entity.IsPlayer`, `entity.ReplaceHealth(...)`, etc.

## Install

From your roblox-csharp project root:

```sh
roblox-csharp plugin add Stiexeno/roblox-csharp-entities
```

That drops the plugin into `plugins/Entities/`. Recompile (`roblox-csharp` or `roblox-csharp dev`) and the runtime mounts at `ReplicatedStorage.Plugins.Entities`.

## Quick start

```csharp
using Entities;
using Entities.CodeGeneration.Attributes;

namespace MyGame
{
    [Game] public class Player : IComponent { }
    [Game] public class Health : IComponent { public int Value; }

    public class DamagePlayerSystem : IExecuteSystem
    {
        private readonly IGroup<GameEntity> _players;

        public DamagePlayerSystem(GameContext game)
        {
            _players = game.GetGroup(GameMatcher.AllOf(GameMatcher.Player, GameMatcher.Health));
        }

        public void Execute()
        {
            foreach (var e in _players)
                e.ReplaceHealth(e.Health - 1);
        }
    }
}
```

Run `roblox-csharp dev`. The plugin generates `Contexts.cs`, `GameContext.cs`, `GameEntity.cs` (with `IsPlayer` / `AddHealth` / `ReplaceHealth` extensions), `GameMatcher.cs`, `GameComponentsLookup.cs`. The transpiler emits Luau, the runtime mounts under `ReplicatedStorage.Plugins.Entities`, your systems run.

## Multiplayer

Tag a component with `[Replicated]` and the codegen wires it to the [networking](https://github.com/Stiexeno/roblox-csharp-networking) plugin: server-side `AddX`/`ReplaceX`/`RemoveX` fire `[NetworkEvent(Scope.ServerToClient)]` delegates, a generated `{Ctx}ClientMirror` subscribes and re-applies on the client. `EntitiesReplication.ShouldEmit()` guards against echoing the apply back over the wire.

## Status

Alpha.

### Shipped

- `IComponent`, `Entity`, `Context`, `Group`, `Matcher` (AllOf / AnyOf / NoneOf chain)
- System lifecycle: `IInitializeSystem`, `IExecuteSystem`, `ICleanupSystem`, `ITearDownSystem`, `Systems`, `Feature`
- Codegen for `[Game]`, `[Input]`, custom `ContextAttribute` subclasses — emits per-context `Context` / `Entity` / `Matcher` / `ComponentsLookup` partials, one file per component
- Flag vs single-`Value` vs multi-field components — each emits the right `IsX` / property+setter / property-only shape
- Entity pool — `Destroy` recycles into `Context._reusableEntities`, next `CreateEntity` pops + `Reactivate`s
- Component pool — `AddX`/`ReplaceX`/setter route through `CreateComponent<T>(index)`; `Context.ClearComponentPool(index)` / `ClearComponentPools()` drain the stacks
- `[Replicated]` codegen — per-context `Replication.cs` + `ClientMirror.cs`; `EntitiesReplication.BeginSuppress/EndSuppress` for nested suppress scopes
- Direct `foreach (var e in group)` via `IGroup<T>:__iter` (no `.GetEntities()` needed)

### Left to do

- `[Unique]` codegen — attribute stub ships but no `context.SetX` / `context.x` accessor wired
- `[PostConstructor]` codegen — attribute stub ships but no post-ctor invocation wired
- `[EntityIndex]` / `[PrimaryEntityIndex]` — neither attribute nor codegen yet
- Property-shaped value components (`public int Value { get; set; }`) — codegen ignores property-backed fields; only plain public fields are picked up
- Replication: `serverTick` is a placeholder (always 0); real tick + client prediction is a follow-up
- Visual debugger

### Won't be added

- `ReactiveSystem` / `Collector` — group iteration covers what I need
- `JobSystem` — no threads on Roblox
- Blueprints, migration tooling
