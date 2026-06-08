---
name: create-service
description: Create a Domain Service — interface + implementation + DI registration. Services encapsulate logic that doesn't fit in systems.
---

Create a domain service for the feature described below.

Service description: $ARGUMENTS

## Steps

### 1. Find the feature folder

Search for `src/Gameplay/` and locate the feature folder. Services go in the `Services/` subfolder.

### 2. Create the interface

Create `I{ServiceName}.cs` in the feature's `Services/` folder.

```csharp
namespace MyGame.Gameplay.{Feature}.Services
{
    public interface I{ServiceName}
    {
        {ReturnType} {MethodName}({params});
    }
}
```

### 3. Create the implementation

Create `{ServiceName}.cs` in the feature's `Services/` folder.

```csharp
namespace MyGame.Gameplay.{Feature}.Services
{
    public class {ServiceName} : I{ServiceName}
    {
        public {ServiceName}()
        {
        }

        public {ReturnType} {MethodName}({params})
        {
            // logic here
        }
    }
}
```

### 4. Register in your DI installer

With `roblox-csharp-dependency-injection`:
```csharp
Container.Bind<I{ServiceName}>().To<{ServiceName}>().AsSingle();
```

## Rules

- Use **tabs** for indentation (size 4), never spaces.
- Use **explicit types** — never `var`.
- Private fields prefixed with `_`.
- Services encapsulate: shared stateless logic, non-ECS dependencies (configs, Roblox-API helpers).
- If shared logic doesn't require external dependencies, it must be an **extension method** in a static class, NOT a service.
- Domain services must be **stateless** and provide pure functions. Only infrastructure services (e.g. `ConfigsService`) can have state.
- Must NEVER directly create or mutate ECS entities — all entity creation goes through factories.
- If a "service" would only create request entities, it should not exist — put the method on the factory.
- May use Queries to read world state.
- **Presentation helpers** (e.g. VFX pooling, audio playback) are allowed but must NOT make gameplay decisions.
- Namespace: `MyGame.Gameplay.{Feature}.Services`.
