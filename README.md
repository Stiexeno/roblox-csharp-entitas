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

Tag a component with `[Replicated]` and the codegen enqueues a Set / Remove op on the server every time `AddX` / `ReplaceX` / `RemoveX` mutates it. One `RemoteEvent` per context lives at `ReplicatedStorage.Plugins.Entities.<Context>Replication`; the runtime drains every context's buffer on `RunService.Heartbeat` and fires once per tick with the batch + a monotonic `serverTick` â€” far cheaper than fanning out per-component delegates and gives you cross-component ordering for free. Large batches split into ~200-op chunks so a single fire stays under Roblox's RemoteEvent payload limit. A generated `{Ctx}ClientReplication` subscribes once and dispatches by `componentIndex` onto the local mirror entity, picking `AddX` vs `ReplaceX` by `HasX` so server re-sends stay idempotent.

`EntitiesReplication.GetServerTick("Game")` returns the highest tick the client has received; `EntitiesReplication.GetTick("Game")` is the server-side counter. The gap is a usable network-distance estimate.

Wire it on boot:

```csharp
public class Bootstrap
{
    public void Start()
    {
        var contexts = new Contexts();
        Contexts.sharedInstance = contexts;

        // Server-only and client-only side-effects each â€” safe to construct
        // both unconditionally; the wrong-side one is a no-op.
        new GameServerReplication(contexts.game);
        new GameClientReplication(contexts.game);
    }
}
```

Late join is handled by a shared `Ready` RemoteEvent: the client fires it once on first `Subscribe`, the server walks every registered `{Ctx}ServerReplication.Snapshot()` and `FireClient`s the resulting Set ops directly to that one player on the per-context channel. The wire is hand-rolled â€” no networking plugin dependency.

## Debugger

Runtime ScreenGui debugger â€” entity inspector, system / feature profiler, group panes, hover-to-highlight â€” adapted from [matter-ecs/matter](https://github.com/matter-ecs/matter)'s plasma debugger. F4 toggles. Plasma is vendored under the plugin runtime, no extra install.

Wire it on boot, after constructing contexts and features:

```csharp
using Entities.Debug;

public class Bootstrap
{
    public void Start()
    {
        var contexts = new Contexts();
        Contexts.sharedInstance = contexts;

        var gameFeature = new GameFeature(contexts);  // your Feature subclass
        // â€¦drive gameFeature.Execute on whichever signal you use (Heartbeat, RenderStepped, â€¦)

        var debugger = new Debugger();
        debugger.AttachContext(contexts.game, GameComponentsLookup.componentNames);
        debugger.AutoInitialize(new Feature[] { gameFeature });
    }
}
```

`AttachContext` is required per context for entity / component visibility â€” pass the codegen-emitted `{Ctx}ComponentsLookup.componentNames` so the UI can label component rows. `AutoInitialize` installs the ScreenGui, the F4 binding (client only), and attaches a per-system profiler to each feature so timings flow without you wiring a signal â€” you keep driving `Feature.Execute` on whatever signal you already use, and the profiler captures wherever it's called. Use `AddProfiledFeatures(...)` for features constructed lazily (e.g., level-load adds features after the initial wire). `Show()` / `Hide()` / `Toggle()` are client-only and equivalent to F4. `RegisterSystemName(typeof(MySystem), "MySystem")` overrides debug.info-based names when source attribution is ambiguous (generic systems generated from a shared module).

### Server view

To inspect server-side entities and systems, construct a `Debugger` on the server too (same `AttachContext` + `AutoInitialize` pattern with the server's features) and call `debugger.SwitchToServerView()` on the client. A per-context RemoteEvent bridges entity / component / profiler snapshots. Outside Studio, server connections are gated â€” the server-side instance rejects every player unless an `authorize(player) â†’ bool` hook is set; see `runtime/Debugger.luau` for the gate.

## Status

Alpha.

### Shipped

- `IComponent`, `Entity`, `Context`, `Group`, `Matcher` (AllOf / AnyOf / NoneOf chain)
- System lifecycle: `IInitializeSystem`, `IExecuteSystem`, `ICleanupSystem`, `ITearDownSystem`, `Systems`, `Feature`
- Codegen for `[Game]`, `[Input]`, custom `ContextAttribute` subclasses â€” emits per-context `Context` / `Entity` / `Matcher` / `ComponentsLookup` partials, one file per component
- Flag vs single-`Value` vs multi-field components â€” each emits the right `IsX` / property+setter / property-only shape
- Entity pool â€” `Destroy` recycles into `Context._reusableEntities`, next `CreateEntity` pops + `Reactivate`s
- Component pool â€” `AddX`/`ReplaceX`/setter route through `CreateComponent<T>(index)`; `Context.ClearComponentPool(index)` / `ClearComponentPools()` drain the stacks
- `[Replicated]` codegen â€” per-context `ServerReplication.cs` (snapshotter for late join) + `ClientReplication.cs` (dispatcher), single per-context RemoteEvent, batched per-frame Heartbeat flush with monotonic `serverTick`, ~200-op chunking for payload-limit safety
- `[Unique]` codegen â€” context-level singleton accessors (`isPlayer` / `playerEntity` / `SetPlayer()` / `UnsetPlayer()`) with auto-track through entity AddX / RemoveX hooks; conflicting assignment throws
- `[PostConstructor]` â€” methods on `partial class Contexts` carrying the attribute are called at the tail of the generated `Contexts()` ctor
- `[EntityIndex]` / `[PrimaryEntityIndex]` â€” field-level. Primary emits `Dictionary<TKey, {Ctx}Entity>` + `GetEntityWith{Component}(TKey)`; non-primary emits `Dictionary<TKey, HashSet<{Ctx}Entity>>` + `GetEntitiesWith{Component}(TKey)`. Entity AddX / ReplaceX / RemoveX / setter bodies tail-update the dict
- `[Watched]` â€” class-level. Synthesizes a `{Name}Changed` flag component, patches state-mutating entity bodies to raise it on Add / Replace / setter (flag flip in either direction), and emits a `{Ctx}WatchedCleanupSystem` you add to the tail of your feature pipeline. Reactive systems gate on `GameMatcher.AllOf(GameMatcher.Health, GameMatcher.HealthChanged)` â€” no polling cache needed. Local-only signal; composes naturally with `[Replicated]` since the client's `Apply{X}` calls `Replace{X}` which raises Changed locally too
- Direct `foreach (var e in group)` via `IGroup<T>:__iter` (no `.GetEntities()` needed)
- `Entity.Destroy()` is virtual; codegen-emitted `{Ctx}Entity` override pre-fires `RemoveX` (or `IsX = false`) on every hooked component before base teardown, so direct destroy keeps `[Replicated]`, `[Unique]`, and `[EntityIndex]` state consistent
- Visual debugger â€” ScreenGui-based, plasma-driven, F4-toggled (see [Debugger](#debugger))

### Known limitations

- **Single world per server.** Replication state (`_buffers`, `_baselines`, ticks) is module-level, keyed by context *name*. Two worlds sharing a context name in one server process would merge their wire state. One game instance per server is the supported shape.
- **Stale entity references.** Destroyed husks are pooled and reused. Reading a destroyed entity errors loudly, but once the husk is reused a held reference silently aliases the *new* entity. Hold entity references within a frame; persist `creationIndex` if you need identity across frames.

### Left to do

- Property-shaped value components (`public int Value { get; set; }`) â€” codegen ignores property-backed fields; only plain public fields are picked up
- Client prediction + reconciliation â€” `serverTick` is on the wire now; the rest of the prediction stack (speculative state, rollback) isn't
- Snapshot delta compression â€” pagination handles payload size, but every Ready ping still sends the full world. A "what's changed since last seen" pass would cut bandwidth on reconnect
- Buffer-typed wire â€” built and reverted: the wire is intentionally Lua-table-shaped (`{opcode, componentIndex, entityId, ...fields}`) so components can carry `Vector3`, `Color3`, `Instance` refs, `CFrame`, and arbitrary custom objects without per-type pack/unpack code. Binary `buffer` packing is the obvious optimization if profiling ever shows the wire is a bottleneck, but it forces a closed type system (every field type needs a schema entry + read/write helper) which loses Roblox's native marshaling for free. Worth ~3â€“5Ã— bandwidth + zero-alloc server pack when you actually need it; for personal-scale Roblox games the table wire is fine
- Group lifecycle hooks (`OnEntityAdded` / `OnEntityRemoved`) â€” useful for reactive systems if/when that pattern lands

### Internal optimizations

- `Context:NotifyComponentChanged` routes through `_groupsByIndex[componentIndex]` so a mutation only touches groups whose matcher includes that index â€” 10â€“50Ã— speedup once you have more than a handful of groups
- Codegen-emitted entity AddX / ReplaceX / RemoveX / setter bodies share one `{ var _ctx = (Ctx)context; if (_ctx != null) {...} }` block for every `[Unique]` + `[EntityIndex]` hook fire, halving the `self:context()` accessor count in Lua

### Won't be added

- `ReactiveSystem` / `Collector` â€” group iteration covers what I need
- `JobSystem` â€” no threads on Roblox
- Blueprints, migration tooling
