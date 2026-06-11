#pragma warning disable CS0626 // Methods are implemented in runtime/Debugger.luau

namespace Entities.Debug
{
	// IDE type-checking surface for the runtime ScreenGui debugger.
	// runtime/Debugger.luau is the real impl; this file exists so consumer
	// C# can write `using Entities.Debug; Debugger.Run(contexts);`.
	//
	// Wire it on boot, BEFORE constructing any Feature:
	//
	//   // Bootstrap.client.cs
	//   var contexts = new Contexts();
	//   Contexts.sharedInstance = contexts;
	//
	//   Debugger.Run(contexts, IsAdmin);   // gate on UserId (see overload)
	//
	//   new GameFeature(contexts);         // auto-attaches profiler
	//
	// F4 toggles. Contexts are walked off `contexts.allContexts`; per-context
	// `componentNames` is read off the context itself (codegen-emitted). Every
	// Feature constructed after Debugger.Run self-registers its profiler via
	// the Debugger singleton — no feature array to maintain, no "did I forget
	// to call AddProfiledFeatures for the level-load feature" bug.
	public static class Debugger
	{
		// Studio: install unconditionally.
		// Live (no authorize): client install is BLOCKED — non-admin players
		// pressing F4 see nothing, and Feature.new sees Current == nil so
		// profiler overhead doesn't fire. Server install proceeds but the
		// "start" remote gate rejects every player.
		// Call this overload only when you want the debugger off in live.
		public static extern void Run(IContexts contexts);

		// Studio: install unconditionally (authorize is ignored).
		// Live client: `authorize(Players.LocalPlayer.UserId)` is called once
		// at this call. If false, Run is a no-op — no GUI, no F4 binding, no
		// profiler overhead. If true, install proceeds.
		// Live server: authorize is stored; the "start" remote consults it
		// with the requesting player's UserId to gate SwitchToServerView.
		// UserId is `long` because Roblox UserIds exceed int32 range. If you
		// need the full Player object inside your check, resolve it via
		// Players.GetPlayerByUserId(id) — keeps this stub from needing the
		// RobloxApi reference.
		public static extern void Run(IContexts contexts, System.Func<long, bool> authorize);

		// Override a system's display name. Useful when debug.info-based
		// resolution returns an ambiguous source path (e.g., generic systems
		// generated from a shared module). Pass the class table, not an
		// instance.
		public static extern void RegisterSystemName(object classTable, string name);

		// Show / Hide / Toggle — client-only. F4 calls Toggle. No-ops if the
		// authorize gate rejected this player.
		public static extern void Show();
		public static extern void Hide();
		public static extern void Toggle();

		public static extern bool IsServerView();
		public static extern void SwitchToServerView();
		public static extern void SwitchToClientView();
	}
}
