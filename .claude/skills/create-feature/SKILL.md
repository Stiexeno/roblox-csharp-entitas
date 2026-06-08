---
name: create-feature
description: Create a Feature class that composes ECS systems into an ordered pipeline. Registers it in GameplayCoreFeature.
---

Create a Feature for the gameplay feature described below.

Feature name: $ARGUMENTS

## Steps

### 1. Find or create the feature folder

Search for `src/Gameplay/` and find or create the feature folder.

### 2. Find existing systems

Search for systems in the feature's `Systems/` subfolder to understand what systems need to be composed.

### 3. Generate the Feature file

Create `{Feature}Feature.cs` in the feature root folder.

```csharp
using Entities;
using MyGame.Gameplay.{Feature}.Systems;

namespace MyGame.Gameplay.{Feature}
{
    public sealed class {Feature}Feature : Feature
    {
        public {Feature}Feature(GameContext game)
        {
            Add(new {System1}(game));
            Add(new {System2}(game));
            // ... ordered pipeline
        }
    }
}
```

With `roblox-csharp-dependency-injection`:
```csharp
public sealed class {Feature}Feature : Feature
{
    public {Feature}Feature(ISystemFactory systemFactory)
    {
        Add(systemFactory.Create<{System1}>());
        Add(systemFactory.Create<{System2}>());
    }
}
```

### 4. Register in GameplayCoreFeature

Find `GameplayCoreFeature.cs` and add the new feature in the correct position:
```csharp
Add(new {Feature}Feature(game));
```

**Ordering principles:**
1. Events must be ready before systems consume them.
2. Input before gameplay logic.
3. State changes before view updates.
4. Cleanup systems always last.

## Rules

- Use **tabs** for indentation (size 4), never spaces.
- A Feature is a **pipeline** — systems are ordered to tell a story.
- The order should read naturally without comments.
- Producer systems before consumer systems.
- Class must be `sealed`.
- Namespace mirrors folder: `namespace MyGame.Gameplay.{Feature}`.
