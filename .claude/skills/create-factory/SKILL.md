---
name: create-factory
description: Create an entity factory — interface + implementation + DI registration. Factories encapsulate entity creation with fully valid entities in one call.
---

Create a factory for the entity described below.

Entity/feature name: $ARGUMENTS

## Steps

### 1. Find the feature folder

Search for `src/Gameplay/` and locate the feature folder. Factories go in the `Services/` subfolder.

### 2. Read existing components

Read the feature's `{Feature}Components.cs` to understand what components the entity has.

### 3. Create the interface

Create `I{Feature}Factory.cs` in the feature's `Services/` folder.

```csharp
using Entities;

namespace MyGame.Gameplay.{Feature}.Services
{
    public interface I{Feature}Factory
    {
        GameEntity Create{Entity}({params});
    }
}
```

### 4. Create the implementation

Create `{Feature}Factory.cs` in the feature's `Services/` folder.

```csharp
using Entities;

namespace MyGame.Gameplay.{Feature}.Services
{
    public class {Feature}Factory : I{Feature}Factory
    {
        private readonly GameContext _game;
        private readonly IIdentifierService _identifierService;

        public {Feature}Factory(
            GameContext game,
            IIdentifierService identifierService)
        {
            _game = game;
            _identifierService = identifierService;
        }

        public GameEntity Create{Entity}({params})
        {
            GameEntity e = _game.CreateEntity();
            e.AddId(_identifierService.Next());
            e.Is{TagComponent} = true;
            e.Add{Component1}(value1);
            e.Add{Component2}(value2);
            return e;
        }
    }
}
```

If your project uses a chained `IEntityFactory` API (similar to the Entitas one), prefer that pattern:
```csharp
return _entityFactory.Game()
    .AddId(_identifierService.Next())
    .With(x => x.Is{TagComponent} = true)
    .Add{Component1}(value1)
    .Add{Component2}(value2);
```

**For saveable entities (created from snapshot):**
```csharp
public GameEntity Create{Entity}({Entity}Snapshot snapshot)
{
    GameEntity e = _game.CreateEntity();
    e.AddId(snapshot.Id);
    e.Is{TagComponent} = true;
    e.Add{Component}(snapshot.{Field});
    return e;
}

public {Entity}Snapshot CreateDefaultSnapshot()
{
    return new {Entity}Snapshot(
        id: 0,
        field: defaultValue);
}
```

### 5. Register in your DI installer

Find `GameplayInstaller.cs` (or your equivalent) and add the binding:

With `roblox-csharp-dependency-injection`:
```csharp
Container.Bind<I{Feature}Factory>().To<{Feature}Factory>().AsSingle();
```

If you're not using DI, the factory will be constructed manually in your bootstrap.

## Rules

- Use **tabs** for indentation (size 4), never spaces.
- Use **explicit types** — never `var`.
- Private fields prefixed with `_`.
- Factories are the ONLY place that creates entities with specific components.
- Factory methods create **fully valid** entities in one call.
- Saveable entities MUST always be created from their Snapshot.
- Factories own request creation for their feature.
- May use configs and identifier services. Must NOT depend on Views directly (can store view-asset keys / Roblox `Instance` refs as component data, but doesn't construct or position them).
- Namespace: `MyGame.Gameplay.{Feature}.Services`.
