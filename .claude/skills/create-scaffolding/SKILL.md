---
name: create-scaffolding
description: Create full feature scaffolding — folders, Components file, Factory, Feature, and (if you use the DI plugin) an Installer.
---

Create a complete feature scaffolding for the feature described below.

Feature name: $ARGUMENTS

## Steps

### 1. Determine names

Given the feature name (e.g. `Combat`), derive:
- `{Feature}` — PascalCase (e.g. `Combat`)
- Feature folder: `src/Gameplay/{Feature}/`

### 2. Create folder structure

Create the following folders under `src/Gameplay/{Feature}/`:
```
{Feature}/
├── Configs/
├── Services/
├── Systems/
```

### 3. Generate `{Feature}Components.cs`

```csharp
using Entities;
using Entities.CodeGeneration.Attributes;

namespace MyGame.Gameplay.{Feature}
{
    [Game] public class {Feature} : IComponent { }
}
```

### 4. Generate `Services/I{Feature}Factory.cs`

```csharp
namespace MyGame.Gameplay.{Feature}.Services
{
    public interface I{Feature}Factory
    {
    }
}
```

### 5. Generate `Services/{Feature}Factory.cs`

```csharp
using Entities;

namespace MyGame.Gameplay.{Feature}.Services
{
    public class {Feature}Factory : I{Feature}Factory
    {
        private readonly GameContext _game;

        public {Feature}Factory(GameContext game)
        {
            _game = game;
        }
    }
}
```

### 6. Generate `{Feature}Feature.cs`

```csharp
using Entities;

namespace MyGame.Gameplay.{Feature}
{
    public sealed class {Feature}Feature : Feature
    {
        public {Feature}Feature(GameContext game)
        {
            // Add(new SomeSystem(game));
        }
    }
}
```

### 7. (Optional) Generate `{Feature}Installer.cs`

If you're using `roblox-csharp-dependency-injection`, create the installer:

```csharp
using MyGame.Gameplay.{Feature}.Services;
using DependencyInjection;

namespace MyGame.Gameplay.{Feature}
{
    public class {Feature}Installer : Installer
    {
        public override void InstallBindings()
        {
            Container.Bind<I{Feature}Factory>().To<{Feature}Factory>().AsSingle();
        }
    }
}
```

### 8. Register in `GameplayCoreFeature`

Find `GameplayCoreFeature.cs` and add:
```csharp
Add(new {Feature}Feature(game));
```

Place it in logical order among existing features.

### 9. Register Installer

Find your top-level gameplay installer and add the line:
```csharp
new {Feature}Installer().InstallBindings();
```

## Rules

- Use **tabs** for indentation (size 4), never spaces.
- Use **explicit types** — never `var`.
- Private fields prefixed with `_`.
- Namespaces mirror folder paths: `namespace MyGame.Gameplay.{Feature}`.
- One class/interface per file.
- Feature folder name is **singular** (e.g. `Combat`, `Player`, `Inventory`).
- The `Systems/` folder is created empty — systems are added later via `/create-system`.
