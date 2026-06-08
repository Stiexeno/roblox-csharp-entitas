---
name: create-system
description: Create an ECS system. Supports IExecuteSystem, IInitializeSystem, ICleanupSystem. Generates the system file and registers it in the Feature.
---

Create an ECS system as described below.

System description: $ARGUMENTS

## Steps

### 1. Determine system type

Based on the description, choose the appropriate system type:

| Base | When to use |
|------|-------------|
| `IExecuteSystem` | Regular per-frame logic (most systems) |
| `IInitializeSystem` | One-time setup: spawning entities, restoring from save |
| `ICleanupSystem` | Resetting boolean flags after they've been consumed |
| `ITearDownSystem` | Disposal when the owning Feature tears down (state exit, scene unload) |

### 2. Find the feature folder

Search for `src/Gameplay/` and locate the feature this system belongs to. Systems go in the `Systems/` subfolder.

If the system is server-only (e.g. spawning, damage application), suffix the file with `.server.cs`. Client-only systems (VFX, UI sync) suffix with `.client.cs`.

### 3. Generate the system file

Create `{SystemName}.cs` (or `.server.cs` / `.client.cs`) in the feature's `Systems/` folder.

**IExecuteSystem template:**
```csharp
using System.Collections.Generic;
using Entities;

namespace MyGame.Gameplay.{Feature}.Systems
{
    public class {SystemName} : IExecuteSystem
    {
        private readonly IGroup<GameEntity> _{groupName};

        public {SystemName}(GameContext game)
        {
            _{groupName} = game.GetGroup(GameMatcher
                .AllOf(
                    GameMatcher.{Component1},
                    GameMatcher.{Component2}));
        }

        public void Execute()
        {
            foreach (GameEntity {entityVar} in _{groupName})
            {
                // logic here
            }
        }
    }
}
```

**IInitializeSystem template:**
```csharp
using Entities;

namespace MyGame.Gameplay.{Feature}.Systems
{
    public class {SystemName} : IInitializeSystem
    {
        public {SystemName}(GameContext game)
        {
        }

        public void Initialize()
        {
            // one-time setup
        }
    }
}
```

**ICleanupSystem template:**
```csharp
using Entities;

namespace MyGame.Gameplay.{Feature}.Systems
{
    public class {SystemName} : ICleanupSystem
    {
        private readonly IGroup<GameEntity> _{groupName};

        public {SystemName}(GameContext game)
        {
            _{groupName} = game.GetGroup(GameMatcher
                .AllOf(
                    GameMatcher.{FlagComponent}));
        }

        public void Cleanup()
        {
            foreach (GameEntity entity in _{groupName})
            {
                entity.Is{FlagComponent} = false;
            }
        }
    }
}
```

### 4. Register in the Feature

Find the appropriate `{Feature}Feature` class and add:
```csharp
Add(new {SystemName}(gameContext));
```

…or via DI if you use `roblox-csharp-dependency-injection`:
```csharp
Add(systemFactory.Create<{SystemName}>());
```

Respect ordering: producers before consumers, state changes before view updates, cleanup last.

## Rules

- Use **tabs** for indentation (size 4), never spaces.
- Use **explicit types** — never `var`.
- Private fields prefixed with `_`.
- Systems must be **stateless** — no mutable instance fields. Only `readonly` groups, services, factories.
- **Single responsibility** — one short sentence describes what the system does.
- **1-2 groups** per system is ideal. **3 is the hard maximum.**
- Entity existence checks: use `group.ContainsEntity(entity)` instead of `entity != null`.
- **Combine existence + flag/component checks into a group.** When the loop body would otherwise do `if (entity == null) continue;` followed by `if (entity.IsFoo == false) continue;` (or `entity.HasFoo == false`), instead define a group whose matcher includes `Foo` and replace both guards with a single `if (_fooGroup.ContainsEntity(entity) == false) continue;`. `ContainsEntity` returns false for null, so the null check is folded in.
- Namespace mirrors folder: `namespace MyGame.Gameplay.{Feature}.Systems`.
- Follow naming convention: `[Subject][Action]System` (e.g., `MarkInCombatSystem`, `ProcessDamageEffectSystem`, `TickAttackSystem`).
- For server-only / client-only logic, suffix the FILE with `.server.cs` / `.client.cs` instead of gating with `if (RunService.IsServer())` runtime checks.
