# roblox-csharp-entitas

[Entitas](https://github.com/sschmid/Entitas)-style ECS for Roblox, packaged as a [roblox-csharp](https://github.com/Stiexeno/roblox-csharp) plugin. Write components and systems the same way you would in Unity: `[Game] class Health : IComponent`, `class FooSystem : IExecuteSystem`, `game.GetGroup(GameMatcher.AllOf(...).NoneOf(...))`. The plugin's codegen runs on every `roblox-csharp dev` tick and writes the usual `Generated/Contexts.cs` / `GameContext.cs` / `GameEntity.cs` / `GameMatcher.cs` / `GameComponentsLookup.cs` partials into `src/Generated/`, so your IDE sees `entity.isPlayer`, `entity.ReplaceHealth(...)`, etc. exactly like in a Unity Entitas project.

## Install

From your roblox-csharp project root:

```sh
roblox-csharp plugin add Stiexeno/roblox-csharp-entitas
```

That drops the plugin into `plugins/Entitas/`. Recompile (`roblox-csharp` or `roblox-csharp dev`) and the runtime mounts at `ReplicatedStorage.Plugins.Entitas`.

## Quick start

```csharp
using Entitas;
using Entitas.CodeGeneration.Attributes;

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
            foreach (var e in _players.GetEntities())
                e.ReplaceHealth(e.health.Value - 1);
        }
    }
}
```

Run `roblox-csharp dev`. The plugin generates `src/Generated/Contexts.cs`, `GameContext.cs`, `GameEntity.cs` (with `isPlayer` / `AddHealth` / `ReplaceHealth` extensions), `GameMatcher.cs`, `GameComponentsLookup.cs`. The transpiler then emits Luau as normal, the runtime mounts under `ReplicatedStorage.Plugins.Entitas`, and your systems run.

## Scope

| Included                                            | Not included                          |
| --------------------------------------------------- | ------------------------------------- |
| `IComponent`, `Entity`, `Context`, `Group`          | `ReactiveSystem`, `Collector`         |
| `Matcher` (AllOf / AnyOf / NoneOf), `IMatcher`      | `JobSystem` (no threads on Roblox)    |
| `IInitializeSystem`, `IExecuteSystem`               | Blueprints, Migration tooling         |
| `ICleanupSystem`, `ITearDownSystem`, `Systems`      | Visual Debugging (planned)            |
| `Feature`                                           |                                       |
| `EntityIndex`, `PrimaryEntityIndex`                 |                                       |
| `[Game]` / `[Input]` / `[Unique]` / `[PostConstructor]` |                                   |
| `[EntityIndex]` / `[PrimaryEntityIndex]`            |                                       |

## Status

Alpha. API matches Entitas 1.14 for the included surface. Frozen-feast-style component / system source compiles verbatim.
