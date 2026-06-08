---
name: create-config
description: Create a config class. Configs hold static data with property getters — no methods, no logic, no lookups.
---

Create a config for the feature described below.

Config description: $ARGUMENTS

## Steps

### 1. Find the feature folder

Search for `src/Gameplay/` and locate the feature folder. Create a `Configs/` subfolder if it doesn't exist.

### 2. Generate the config class

Create `{ConfigName}.cs` in the feature's `Configs/` folder.

```csharp
namespace MyGame.Gameplay.{Feature}.Configs
{
    public class {ConfigName}
    {
        public {Type} {PropertyName} { get; }

        public {ConfigName}({type} {paramName})
        {
            {PropertyName} = {paramName};
        }
    }
}
```

For purely static data (compiled constants that never need to vary per environment):

```csharp
namespace MyGame.Gameplay.{Feature}.Configs
{
    public static class {ConfigName}
    {
        public const int MaxHealth = 100;
        public const float MoveSpeed = 16f;
    }
}
```

### 3. Source the config data

Roblox doesn't have ScriptableObjects. Pick the source that matches the config's needs:

- **Compile-time constants** → `public const` fields in a static class
- **Per-environment values** (debug vs prod, A/B tested) → JSON in a `ReplicatedStorage` ModuleScript or `MessagingService` payload, parsed at boot
- **Live-tunable values** (designer can edit at runtime without recompiling) → `MemoryStoreService` / `DataStoreService` polled at boot or on a designer-triggered refresh

### 4. Add to a ConfigsService (if many configs)

If you have many configs for this feature, create `I{Feature}ConfigsService` following the service pattern:

```csharp
namespace MyGame.Gameplay.{Feature}.Services
{
    public interface I{Feature}ConfigsService
    {
        {ConfigName} Get{ConfigName}({KeyType} key);
    }
}
```

…with the service holding a `Dictionary<{KeyType}, {ConfigName}>` populated at boot.

## Rules

- Use **tabs** for indentation (size 4), never spaces.
- Configs are **read-only after loading**. Runtime state belongs on entities, not configs.
- **No methods, no logic, no lookups** — derived values go into a config service.
- Public properties with getters only (or `const` fields for the static case).
- Config classes go in `{Feature}/Configs/`.
- Namespace: `MyGame.Gameplay.{Feature}.Configs`.
- If the config needs a single instance accessible everywhere, bind it as a singleton in the DI installer.
