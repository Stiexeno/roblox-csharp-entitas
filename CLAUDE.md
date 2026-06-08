# CLAUDE.md

Guidance for Claude Code when working with games built on the `roblox-csharp-entities` plugin (or with the plugin itself).

## Project Overview

ECS for Roblox via the [roblox-csharp](https://github.com/Stiexeno/roblox-csharp) C# → Luau transpiler. The plugin gives you:

- `IComponent`, `Entity`, `Context`, `Group`, `Matcher` (AllOf / AnyOf / NoneOf chain)
- System lifecycle: `IInitializeSystem`, `IExecuteSystem`, `ICleanupSystem`, `ITearDownSystem`, `Systems`, `Feature`
- Per-context codegen for `[Game]` / `[Input]` / any custom `ContextAttribute` subclass
- `[Replicated]`, `[Unique]`, `[PostConstructor]`, `[EntityIndex]`, `[PrimaryEntityIndex]` attributes
- Entity pool, component pool, group routing, automatic `Destroy` cleanup
- Server-authoritative replication with late-join snapshot

Companion plugins commonly used together:
- `roblox-csharp-dependency-injection` (Zenject-style; `ServerInstaller`/`ClientInstaller`)
- `roblox-csharp-state-management` (`IGameStateMachine`, `IState`, `IEnter`/`IExit`/`IExecutable`)
- `roblox-csharp-networking` (raw `[NetworkEvent]` fields; this plugin doesn't use it — it has its own buffer-shaped wire)
- `roblox-csharp-maid`, `roblox-csharp-linq`, `roblox-csharp-tween`

## Code Generation

The entities plugin's codegen runs on every `roblox-csharp dev` tick. Output lands in `src/Generated/`:

- `Contexts.cs`, `{Ctx}Context.cs`, `{Ctx}Entity.cs`, `{Ctx}Matcher.cs`, `{Ctx}ComponentsLookup.cs`
- `Generated/Components/{Ctx}.{Component}.cs` — per-(context, component) partials (entity property + matcher static + optional `[Unique]` / `[EntityIndex]` context-side state)
- `Generated/{Ctx}ServerReplication.cs` + `Generated/{Ctx}ClientReplication.cs` — only when the context has `[Replicated]` components

Do NOT manually edit anything under `src/Generated/`. Re-runs on every dev tick; your edits will be lost.

## Architecture — Three Layers

| Layer | Contains | Responsibility |
|-------|----------|---------------|
| **View** | Roblox `Instance`s, UI, VFX, audio | Display state and react to it. NO game rules. |
| **Domain** | ECS systems, factories, queries, services, state machine | ALL game logic lives here. |
| **Storage** | `DataStoreService` wrappers, Snapshots (DTOs) | Persistence only. No gameplay logic. |

### Communication between layers

- **View → Domain (read):** Queries (polling) — reactive queries aren't supported yet by the plugin
- **View → Domain (write):** Service / state-machine method calls OR ECS request entities
- **Domain → View:** Systems may call view-side methods only for lifecycle (attach/detach/destroy) or visual triggers (play VFX). NOT for game rules
- **Domain → Storage:** Reads snapshots on load, writes only via a dedicated `RefreshSnapshotsFeature` on save
- **View → Storage:** never — always go through Domain

## Server / Client Split

Files ending in `.server.cs` become Roblox Server Scripts; `.client.cs` become Local Scripts. Plain `.cs` are ModuleScripts replicated to both sides. Use the split for entry points and for any code that should only execute on one side.

A typical entry-point pair:

```
src/Bootstrap.server.cs   → spawns server-only systems, GameServerReplication
src/Bootstrap.client.cs   → spawns client-only systems, GameClientReplication
src/Bootstrap.shared.cs   → shared init (Contexts.sharedInstance, etc.)
```

Replication classes (`GameServerReplication`, `GameClientReplication`) are no-ops on the wrong runtime side, so unconditional construction in shared code is fine too — the wire just doesn't fire there.

## Code Style

Defined in `.claude/rules/code-style.md` (auto-loaded when editing `.cs` files). Highlights:

- Tabs (size 4), max 120 chars, explicit types only (no `var`), `_camelCase` private fields
- Conditions: `== false` instead of `!`
- Events: `OnX` for the event, `HandleX` for handlers, never lambdas as handlers
- Class member order: fields → injected fields → constants → properties → events → ctor → lifecycle → execute → handlers
- Systems must be **stateless** (no mutable instance fields)
- One class/interface per file
- Namespaces mirror folder paths

## Roblox / Transpiler Quirks

Defined in `.claude/rules/roblox-csharp-quirks.md`. Highlights:

- `out var` doesn't lower today — use `ContainsKey + indexer` instead of `TryGetValue`
- `params object[]` works; explicit overloads still preferred when the wire shape matters
- Roblox `Instance` references pass through `RemoteEvent` payloads natively (Lua-table wire)
- `task.spawn` / `task.delay` instead of `UniTask`
- No `ScriptableObject` — configs are plain C# classes (`public static` constants or DI-bound instances loaded from JSON / `MemoryStore`)

## Testing

Project structure follows the plugin's own test pattern:
- **Framework:** xUnit + FluentAssertions if you want it (Plugin uses bare xUnit)
- **Location:** `tests/`
- **Pattern:** test the codegen output shape against synthetic user sources; test runtime behaviour via the transpiled-Luau pipeline when needed
- **Tear-down:** call `_game.DestroyAllEntities()` after each test

## Key Patterns

**Entities created through factories** using `IEntityFactory` (provide your own via DI):
- `_entityFactory.Game()` — regular entity
- `_entityFactory.Event()` — tagged `isEvent`, auto-destroyed after one frame by an `EventsCleanupSystem`
- `_entityFactory.Request()` — tagged `isRequest`, must be destroyed by its handler

**Requests** (many-to-one): created from many places, handled by a single system. Use a `RequestHandlerSystem` base class (write your own, similar to the Entitas one) for automatic cleanup.

**Events** (one-to-many): created by one system, consumed by many. Get a `Ready` flag next frame so all systems see them regardless of order.

**System ordering matters** — systems execute in the order added to a Feature. Producer before consumer. See the architect skill for ordering principles.

## DI Registration (when using `roblox-csharp-dependency-injection`)

- `ServerInstaller` / `ClientInstaller` for per-side bindings
- Shared installer for ECS contexts, entity/system factories, identifier service
- Plain C# classes use constructor injection
- Queries registered with `BindInterfacesTo<>()` so any future `IReactiveQuery` implementations would be auto-collected
