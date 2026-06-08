---
name: create-request
description: Create a Request — component + RequestHandlerSystem + registration. Requests are many-to-one fire-and-forget commands.
---

Create a Request for the action described below.

Request description: $ARGUMENTS

## Steps

### 1. Determine the feature

Identify which feature this request belongs to. Search for `src/Gameplay/` and locate the feature folder.

### 2. Add the request component

Read the feature's `{Feature}Components.cs` file and add the request component:

**Flag request (no data):**
```csharp
[Game] public class RestartGameRequest : IComponent { }
```

**Data request:**
```csharp
[Game] public class SpawnEnemyRequest : IComponent { public EnemyTypeId Value; }
```

### 3. Create the handler system

Create the handler in the feature's `Systems/` folder.

```csharp
using Entities;

namespace MyGame.Gameplay.{Feature}.Systems
{
    public class {HandlerName} : IExecuteSystem
    {
        private readonly GameContext _game;
        private readonly IGroup<GameEntity> _requests;

        public {HandlerName}(GameContext game)
        {
            _game = game;

            _requests = game.GetGroup(GameMatcher
                .AllOf(
                    GameMatcher.Request,
                    GameMatcher.{RequestComponent}));
        }

        public void Execute()
        {
            foreach (GameEntity request in _requests)
            {
                // handle the request
                // ...

                // Then destroy it so it doesn't fire next tick:
                request.Destroy();
            }
        }
    }
}
```

If you have your own `RequestHandlerSystem<TEntity>` base class that auto-destroys requests after `OnExecute`, use it instead — keeps handler bodies clean.

### 4. Register in Feature

Add the handler system to the feature's Feature class:
```csharp
Add(new {HandlerName}(game));
```

### 5. Show how to create the request

Provide the caller pattern:

**From a system:**
```csharp
_entityFactory.Request()
    .With(x => x.IsRestartGameRequest = true);
```

**From a view (client-side UI):**
```csharp
_entityFactory.Request()
    .Add{RequestComponent}(value);
```

For client → server requests, you'll need a separate RemoteEvent or use a `[Replicated]` request component that the server picks up and handles. The `[Replicated]` route requires the server side of replication to interpret incoming Sets as request-creation — not built into the plugin by default.

## Rules

- Use **tabs** for indentation (size 4), never spaces.
- Create requests with `_entityFactory.Request()` (NOT `.Game()`). Tags entity with `isRequest` for orphan detection.
- Handler MUST destroy request entities after handling — undestroyed requests re-trigger every frame.
- Request components are suffixed with `Request`.
- One handler system per request type.
- If multiple places need to create the same request, put a helper method on the relevant factory.
