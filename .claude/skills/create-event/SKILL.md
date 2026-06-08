---
name: create-event
description: Create an Event — component + producer/consumer pattern. Events are one-to-many notifications auto-destroyed after one frame.
---

Create an Event for the scenario described below.

Event description: $ARGUMENTS

## Steps

### 1. Determine the feature

Identify which feature produces this event. Search for `src/Gameplay/` and locate the feature folder.

### 2. Add the event component

Read the feature's `{Feature}Components.cs` and add the event component:

```csharp
[Game] public class {Name}Event : IComponent { public int {RelevantId}; }
```

For a server-replicated event (lets clients react too), add `[Replicated]`:
```csharp
[Game, Replicated] public class DeathEvent : IComponent { public int DeadEntityId; }
```

### 3. Show the producer pattern

The system that creates the event:

```csharp
_entityFactory.Event().Add{Name}Event(entityId);
```

Events should get a `Ready` flag next frame (via your own `EventsReadySystem`) so ALL systems see them regardless of ordering. If you don't have that infrastructure yet, the consumer must run after the producer in the Feature pipeline.

### 4. Create consumer system(s)

Create the consumer in the appropriate feature's `Systems/` folder.

```csharp
using Entities;

namespace MyGame.Gameplay.{ConsumerFeature}.Systems
{
    public class {Action}On{Event}System : IExecuteSystem
    {
        private readonly GameContext _game;
        private readonly IGroup<GameEntity> _{eventGroup};
        private readonly IGroup<GameEntity> _{targetGroup};

        public {Action}On{Event}System(GameContext game)
        {
            _game = game;

            _{eventGroup} = game.GetGroup(GameMatcher
                .AllOf(
                    GameMatcher.{Name}Event));

            _{targetGroup} = game.GetGroup(GameMatcher
                .AllOf(
                    GameMatcher.{TargetMatcher}));
        }

        public void Execute()
        {
            foreach (GameEntity eventEntity in _{eventGroup})
            {
                GameEntity target = _game.GetEntityWithId(eventEntity.{eventAccessor}.{RelevantId});

                if (_{targetGroup}.ContainsEntity(target))
                {
                    // react to event
                }
            }
        }
    }
}
```

### 5. Register in Feature

Add consumer systems to the appropriate Feature class. Consumer systems must be ordered AFTER the producer (or after `EventsReadySystem` if you have one).

## Rules

- Use **tabs** for indentation (size 4), never spaces.
- Create events with `_entityFactory.Event()` — tagged `isEvent`.
- Do NOT manually destroy event entities — an `EventsCleanupSystem` should handle this each frame.
- Event components are suffixed with `Event`.
- Consumer system naming: `{Action}On{Event}System`.
- Avoid events for high-frequency per-frame signals — use a component on an existing entity instead.
- Entity existence checks: use `group.ContainsEntity(entity)` instead of `entity != null`.
- If the event needs to fire VFX on every client (e.g. damage hit, ability cast), mark the event component `[Replicated]`. Server adds it → wire delivers it to clients → client systems react.
