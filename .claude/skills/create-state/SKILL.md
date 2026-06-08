---
name: create-state
description: Create a game state for the state machine. States implement IState plus optional IEnter, IExit, IExecutable.
---

Create a game state as described below.

State description: $ARGUMENTS

Requires `roblox-csharp-state-management` plugin.

## Steps

### 1. Read existing states

Search for the `Infrastructure/StateManagement/States/` folder to understand the current state flow and patterns.

### 2. Determine interfaces

Based on the state's responsibility, choose which interfaces to implement:

| Interface | Purpose |
|-----------|---------|
| `IState` | Required for all states |
| `IEnter` | Has `Enter()` — runs once when entering the state |
| `IExit` | Has `Exit()` — runs once when leaving the state |
| `IExecutable` | Has `Execute()` — called every frame (Heartbeat) |

### 3. Generate the state file

Create `{StateName}.cs` in the `Infrastructure/StateManagement/States/` folder.

**Simple state (no per-frame work):**
```csharp
using StateManagement;

namespace MyGame.Infrastructure.StateManagement.States
{
    public class {StateName} : IState, IEnter
    {
        private readonly IGameStateMachine _gameStateMachine;

        public {StateName}(IGameStateMachine gameStateMachine)
        {
            _gameStateMachine = gameStateMachine;
        }

        public void Enter()
        {
            // setup work
            _gameStateMachine.Enter<{NextState}>();
        }
    }
}
```

**Gameplay state (owns Features + per-frame update):**
```csharp
using Entities;
using StateManagement;
using MyGame.Gameplay;

namespace MyGame.Infrastructure.StateManagement.States
{
    public class {StateName} : IState, IEnter, IExecutable, IExit
    {
        private readonly GameContext _game;
        private {Feature}Feature _{featureName};

        public {StateName}(GameContext game)
        {
            _game = game;
        }

        public void Enter()
        {
            _{featureName} = new {Feature}Feature(_game);
            _{featureName}.Initialize();
        }

        public void Execute()
        {
            _{featureName}.Execute();
            _{featureName}.Cleanup();
        }

        public void Exit()
        {
            _{featureName}.TearDown();
            _{featureName} = null;
        }
    }
}
```

### 4. Register the state

Find where states are registered with the state machine and add the new state registration.

### 5. Wire the transition

Update the previous state to transition to this new state, or update this state to transition to the next state.

## Rules

- Use **tabs** for indentation (size 4), never spaces.
- Use **explicit types** — never `var`.
- Private fields prefixed with `_`.
- States implement `IState` plus optional lifecycle interfaces.
- Typical state flow: `BootstrapState` → `LoadProgressState` → `PrepareGameplayState` → `GameplayState`.
- Gameplay states own Features and call `Initialize()`, `Execute()`, `Cleanup()`, `TearDown()`.
- Namespace: `MyGame.Infrastructure.StateManagement.States`.
