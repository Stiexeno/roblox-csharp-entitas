---
paths:
  - "**/*.cs"
description: C# code style rules for roblox-csharp-entities games and plugin code
---

## Code Style (enforced)

- **Tabs** (size 4), not spaces. Max 120 chars per line.
- **Explicit types** — never use `var`.
- **Private fields** prefixed with `_`. (No `[SerializeField]` equivalent in Roblox — every field is a normal C# field.)
- **No abbreviations** (use `Position` not `Pos`, `Rotation` not `Rot`). Exception: `Id`.
- **Conditions:** use `== false` instead of `!`.
- **Events:** named `OnX`, handlers named `HandleX`. No lambda event handlers.
- **Max 3 method params** — extract to a struct (suffixed `Request` / `Response` / `Dto`) if more. Exception: constructors and DI `Construct` methods.
- **Named arguments** each on own line when used.
- **Class member order:** internal variables → injected fields → constants → properties → events → constructor → lifecycle (`Initialize`, etc.) → subscribe/unsubscribe → setup/cleanup → update/execute → internal methods → handlers (`HandleX`).
- **Systems must be stateless** — no instance fields that change over time. Only `readonly` groups, buffers, services, factories.
- **One class/interface per file** (exception: generic overloads with the same name).
- **Namespaces** mirror folder paths: `namespace MyGame.Gameplay.Effects.Systems`.

## Roblox-specific notes

- No `MonoBehaviour` or `ScriptableObject`. Files ending in `.server.cs` lower to Server Scripts, `.client.cs` to Local Scripts, plain `.cs` to ModuleScripts replicated to both sides.
- Use the transpiler's supported subset — see `.claude/rules/roblox-csharp-quirks.md` for the gotchas (no `out var`, no `TryGetValue`, careful with `LINQ`, etc.).
- Roblox API stubs (Players, RunService, ReplicatedStorage, etc.) come from `roblox-csharp-roblox-api`. Reference them as if they were normal C# types — the transpiler lowers them to Roblox calls.
- `Instance` references can pass through `RemoteEvent` payloads natively. Don't try to serialize them yourself.
