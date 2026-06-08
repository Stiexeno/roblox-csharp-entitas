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

Tag a component with `[Replicated]` and the codegen enqueues a Set / Remove op on the server every time `AddX` / `ReplaceX` / `RemoveX` mutates it. The op is binary-packed into a Roblox `buffer` per the registered schema — `1 byte opcode + 1 byte componentIndex + 4 byte entityId + fields`. One `RemoteEvent` per context lives at `ReplicatedStorage.Plugins.Entities.<Context>Replication`; the runtime drains every context's buffer on `RunService.Heartbeat` and fires once per tick with `(serverTick, count, buffer)`. ~3–5× smaller than the old Lua-table wire, near-zero per-tick GC on the server pack path, cross-component ordering preserved by single-RemoteEvent semantics. A generated `{Ctx}ClientReplication` registers typed `Apply{X}` / `Remove{X}` callbacks; the runtime unpacks each received op per schema and calls them, picking `AddX` vs `ReplaceX` by `HasX` so server re-sends stay idempotent.

`EntitiesReplication.GetServerTick("Game")` returns the highest tick the client has received; `EntitiesReplication.GetTick("Game")` is the server-side counter. The gap is a usable network-distance estimate.

Wire it on boot:

```csharp
public class Bootstrap
{
    public void Start()
    {
        var contexts = new Contexts();
        Contexts.sharedInstance = contexts;

        // Server-only and client-only side-effects each — safe to construct
        // both unconditionally; the wrong-side one is a no-op.
        new GameServerReplication(contexts.game);
        new GameClientReplication(contexts.game);
    }
}
```

Late join is handled by a shared `Ready` RemoteEvent: the client fires it once on first `Subscribe`, the server walks every registered `{Ctx}ServerReplication.Snapshot()` and `FireClient`s the resulting Set ops directly to that one player on the per-context channel. The wire is hand-rolled — no networking plugin dependency.

## Status

Alpha.

### Shipped

- `IComponent`, `Entity`, `Context`, `Group`, `Matcher` (AllOf / AnyOf / NoneOf chain)
- System lifecycle: `IInitializeSystem`, `IExecuteSystem`, `ICleanupSystem`, `ITearDownSystem`, `Systems`, `Feature`
- Codegen for `[Game]`, `[Input]`, custom `ContextAttribute` subclasses — emits per-context `Context` / `Entity` / `Matcher` / `ComponentsLookup` partials, one file per component
- Flag vs single-`Value` vs multi-field components — each emits the right `IsX` / property+setter / property-only shape
- Entity pool — `Destroy` recycles into `Context._reusableEntities`, next `CreateEntity` pops + `Reactivate`s
- Component pool — `AddX`/`ReplaceX`/setter route through `CreateComponent<T>(index)`; `Context.ClearComponentPool(index)` / `ClearComponentPools()` drain the stacks
- `[Replicated]` codegen — per-context `ServerReplication.cs` (snapshotter + schema registration) + `ClientReplication.cs` (typed appliers + schema mirror), single per-context RemoteEvent, binary-packed Roblox `buffer` wire, batched per-frame Heartbeat flush with monotonic `serverTick`
- `[Unique]` codegen — context-level singleton accessors (`isPlayer` / `playerEntity` / `SetPlayer()` / `UnsetPlayer()`) with auto-track through entity AddX / RemoveX hooks; conflicting assignment throws
- `[PostConstructor]` — methods on `partial class Contexts` carrying the attribute are called at the tail of the generated `Contexts()` ctor
- `[EntityIndex]` / `[PrimaryEntityIndex]` — field-level. Primary emits `Dictionary<TKey, {Ctx}Entity>` + `GetEntityWith{Component}(TKey)`; non-primary emits `Dictionary<TKey, HashSet<{Ctx}Entity>>` + `GetEntitiesWith{Component}(TKey)`. Entity AddX / ReplaceX / RemoveX / setter bodies tail-update the dict
- Direct `foreach (var e in group)` via `IGroup<T>:__iter` (no `.GetEntities()` needed)
- `Entity.Destroy()` is virtual; codegen-emitted `{Ctx}Entity` override pre-fires `RemoveX` (or `IsX = false`) on every hooked component before base teardown, so direct destroy keeps `[Replicated]`, `[Unique]`, and `[EntityIndex]` state consistent

### Left to do

- Property-shaped value components (`public int Value { get; set; }`) — codegen ignores property-backed fields; only plain public fields are picked up
- Client prediction + reconciliation — `serverTick` is on the wire now; the rest of the prediction stack (speculative state, rollback) isn't
- Snapshot delta compression — pagination handles payload size, but every Ready ping still sends the full world. A "what's changed since last seen" pass would cut bandwidth on reconnect
- Visual debugger
- Group lifecycle hooks (`OnEntityAdded` / `OnEntityRemoved`) — useful for reactive systems if/when that pattern lands

### Internal optimizations

- `Context:NotifyComponentChanged` routes through `_groupsByIndex[componentIndex]` so a mutation only touches groups whose matcher includes that index — 10–50× speedup once you have more than a handful of groups
- Codegen-emitted entity AddX / ReplaceX / RemoveX / setter bodies share one `{ var _ctx = (Ctx)context; if (_ctx != null) {...} }` block for every `[Unique]` + `[EntityIndex]` hook fire, halving the `self:context()` accessor count in Lua

### Won't be added

- `ReactiveSystem` / `Collector` — group iteration covers what I need
- `JobSystem` — no threads on Roblox
- Blueprints, migration tooling
