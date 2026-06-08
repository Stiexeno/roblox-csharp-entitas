---
name: create-components
description: Create ECS components file for a gameplay feature. Generates the components file with proper context attributes, naming, and placement.
---

Create ECS components for the feature described below.

Feature/entity name: $ARGUMENTS

## Steps

### 1. Determine feature location

Search for `src/Gameplay/` in the project and find or create the feature folder matching the name.

### 2. Check for existing components file

Look for `{Feature}Components.cs` in the feature folder. If it exists, read it and add new components to it. If not, create it.

### 3. Generate the components file

Create `{Feature}Components.cs` in the feature root folder.

**Flag component (no fields):**
```csharp
[Game] public class Dead : IComponent { }
```

**Single-value component:**
```csharp
[Game] public class CurrentHP : IComponent { public float Value; }
```

**Multi-field component (use sparingly — prefer atomic):**
```csharp
[Game] public class Position : IComponent
{
    public float X;
    public float Y;
}
```

**Request component:**
```csharp
[Game] public class SpawnEnemyRequest : IComponent { public EnemyTypeId Value; }
```

**Event component:**
```csharp
[Game] public class DeathEvent : IComponent { public int DeadEntityId; }
```

**Replicated component (server → client wire):**
```csharp
[Game, Replicated] public class Health : IComponent { public int Value; }
```

**Indexed lookup component (Primary = one entity per key):**
```csharp
[Game]
public class Player : IComponent
{
    [PrimaryEntityIndex] public int UserId;
}
// generates gameContext.GetEntityWithPlayer(userId) — O(1)
```

**Watched component (reactive change signal):**
```csharp
[Game, Watched] public class Health : IComponent { public int Value; }
// generates a `HealthChanged` flag and patches the entity's AddHealth /
// ReplaceHealth / setter to `IsHealthChanged = true`. Reactive systems
// gate on `GameMatcher.AllOf(GameMatcher.Health, GameMatcher.HealthChanged)`
// to see only entities whose Health changed this frame. Don't forget to
// add `new GameWatchedCleanupSystem(game)` to the tail of your feature
// pipeline so the Changed flag clears at end of frame.
```

### Full file template

```csharp
using Entities;
using Entities.CodeGeneration.Attributes;

namespace MyGame.Gameplay.{Feature}
{
    [Game] public class {TagComponent} : IComponent { }
    // ... additional components
}
```

## Rules

- Use **tabs** for indentation (size 4), never spaces.
- Attribute with `[Game]` or `[Input]` context (or your own `ContextAttribute` subclass).
- Prefer **atomic** components (single field or flag). 99% of regular components should be atomic.
- Use **tag components** for querying: `isPlayer`, `isEnemy`, `isDead`.
- If name collides with an enum (e.g. `EffectTypeId`), suffix the class with `Component`.
- Single-value components use `Value` as the field name (unlocks the unwrap accessor: `entity.Health` returns `int`, not the component).
- One components file per feature — all components for the feature live in one file.
- Namespace mirrors folder path: `namespace MyGame.Gameplay.{Feature}`.
- **Replicated wire types:** anything Roblox marshals natively works — primitives, `string`, `Vector3`, `Color3`, `CFrame`, `UDim2`, `Instance` refs, nested user structs, arrays/lists. Don't replicate `Position` if a `BasePart` already lives at that position (Roblox physics replication handles it).
- Codegen runs on `roblox-csharp dev`. New components show up immediately — no manual generation step.
