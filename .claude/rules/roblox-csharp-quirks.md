---
paths:
  - "**/*.cs"
description: Transpiler-specific quirks when writing C# that lowers to Luau
---

# roblox-csharp transpiler quirks

The roblox-csharp transpiler covers most of C# but has gaps and Roblox-specific routing rules. Avoid the patterns below.

## Language features — what lowers, what doesn't

Each entry below has a probe test pinning the behavior in `tests/TranspilerProbeTests.cs` — run them if you upgrade the converter to catch regressions or new support.

### ✅ Works

- **`async` / `await`** — `AwaitExpressionTransformer` unwraps `await foo()` to `foo()`. The `async` keyword on the method signature is a no-op on lowering. True coroutine wrapping (per the renderer's TODO list) isn't done yet, so `await` is structural compatibility — the call runs synchronously in Luau. If you need real concurrency, use Roblox's `task.spawn(fn)` / `task.delay(seconds, fn)` / `task.wait(seconds)`.
- **`out` parameters in method declarations** — `int Calculate(ref int a, out int b, in int c, params int[] values)` compiles fine. Confirmed in `RobloxCSharp.Tests/.../Kitchen.cs`.
- **Pattern matching with `is X y`** — `IsPatternExpressionTransformer` handles `DeclarationPatternSyntax` and binds the variable via `AddPrerequisite(LocalDeclaration(...))`. So `if (obj is int i) { use(i); }` works.
- **Tuple deconstruction in declarations** — `(int a, int b) = method();` works; both locals are bound.

### ❌ Doesn't lower

- **`out var v` / `out int v` at a call site** — `DeclarationExpressionSyntax` has no transformer. The transpiler emits a literal `--[[ NotImplemented: DeclarationExpressionSyntax ]]` comment in the argument position and any subsequent reference to `v` is a phantom local. Use `ContainsKey + indexer` instead:
  ```csharp
  // BAD — emits NotImplemented + undefined `v` reference
  if (dict.TryGetValue(key, out int v)) { use(v); }

  // GOOD
  if (dict.ContainsKey(key)) {
      int v = dict[key];
      use(v);
  }
  ```

## Numeric / indexing

- **Luau is 1-based** but the transpiler emits 0-based access for C# arrays/lists. The plugin's stdlib calls are inlined as macros — don't second-guess them.
- **`int` is Luau `number`** (double under the hood). Bit-width matters at the wire / DataStore boundary, not in memory.

## LINQ

- The `roblox-csharp-linq` plugin covers `Select`, `Where`, `Sum`, `ToList`, etc. Stick to that subset.
- `Enumerable.Empty<T>()` may or may not be covered — check before using. Safer to return `null` from a "no match" lookup and have the caller null-check.
- `IEnumerable<T>` works as a return type but iterating it allocates a Lua closure per `foreach`. For hot paths prefer `T[]` or `List<T>`.

## Roblox API access

- Roblox API stubs come from `roblox-csharp-roblox-api`. Reference them as plain C# types:
  ```csharp
  using ReplicatedStorage = ...;  // namespace via the stub
  Players.GetPlayerByUserId(123);
  RunService.Heartbeat.Connect(_ => ...);
  ```
- `Instance` references **pass through `RemoteEvent` payloads natively** — both sides see the same Roblox instance. Don't try to serialize them via JSON or anything similar.
- `BasePart` / `Model` / etc. are real Roblox objects. The View layer holds references; Domain components hold logic-state data.

## Replication (`[Replicated]` components)

- Field types supported by the wire (Lua-table-shaped, marshals natively): all primitives, `string`, `Vector3`, `Color3`, `CFrame`, `UDim2`, `Instance` refs, nested user structs, nested tables, arrays/lists of any of the above.
- The codegen-emitted `{Ctx}ServerReplication` and `{Ctx}ClientReplication` classes wire themselves on construction. Construct both in shared code; the wrong-side one is a no-op (the runtime guards on `RunService.IsServer()` internally).
- **Position in `[Replicated]` components is usually wrong.** Roblox already replicates `BasePart.Position`. Replicating a `Position : IComponent { Vector3 Value; }` would double-send. Hold logical state (`Health`, `Score`, `Team`, `IsStunned`) in components; let Roblox physics handle the visual position. Link the two via a non-replicated `View` component on the client.

## Generic methods

- The transpiler erases generic type parameters and passes them as the **first runtime argument** to the lowered method (commit `3613bf0` in the converter). When wrapping a generic method, the wrapper signature in Lua starts with `_T`.

## Methods that don't exist in Roblox

- No `Console.WriteLine` — use `print(...)` (or `warn(...)`, `error(...)`).
- No `System.Threading` — use Roblox's `task.*`.
- No `System.IO` — read/write via `DataStoreService`, `MessagingService`, or `HttpService`.
- No reflection at runtime — `typeof(X)` works as a class reference for the transpiler's own dispatch, not for general .NET reflection.

## Things that DO transpile cleanly (don't be afraid to use them)

- `partial class` — central to the codegen pattern; the transpiler merges all partials of a class into one Luau metatable.
- `internal` / `protected internal` accessibility — preserved structurally even though Luau has no real visibility.
- `default(T)` for value-type defaults.
- `Dictionary<TKey, TValue>` / `HashSet<T>` / `List<T>` — common BCL collections work via the converter's BCL transformer overrides.
- `foreach (var x in collection)` — lowers to Luau's generalized-for. Works on arrays, lists, dicts, and on `IGroup<T>` (via the runtime's `__iter` metamethod).
