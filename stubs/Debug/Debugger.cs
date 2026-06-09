#pragma warning disable CS0626 // Methods are implemented in runtime/Debug/Debugger.luau

namespace Entities.Debug
{
	// IDE type-checking surface for the runtime ScreenGui debugger.
	// runtime/Debug/Debugger.luau is the real impl; this file exists so
	// consumer C# can write `using Entities.Debug; new Debugger();`.
	//
	// Plasma is required internally by Debugger.luau, so the ctor is
	// no-arg. Wire up like this:
	//
	//   // Bootstrap.client.cs
	//   Debugger debugger = new Debugger();
	//   debugger.AttachContext(Contexts.sharedInstance.game, GameComponentsLookup.componentNames);
	//   debugger.AutoInitialize(RunService.Heartbeat, new Feature[] { _gameFeature });
	//   debugger.Show();
	//
	// F4 toggles. AttachContext is required per context for entity/
	// component visibility; AutoInitialize wires the plasma frame loop
	// and the F4 binding; Show enables drawing.
	public class Debugger
	{
		public extern Debugger();

		// componentsLookup is the codegen-emitted GameComponentsLookup.componentNames
		// (a string[] indexed by componentIndex). Required so the UI can label
		// component rows by name.
		public extern void AttachContext(IContext context, string[] componentsLookup);

		// Override a system's display name. Useful when debug.info-based
		// resolution returns an ambiguous source path (e.g., generic systems
		// generated from a shared module). Pass the class table, not an
		// instance.
		public extern void RegisterSystemName(object classTable, string name);

		// One-time setup: ScreenGui, F4 binding, Heartbeat-driven plasma
		// frames, and a profiler hooked onto each feature so per-system
		// timing flows without the user passing a signal. The user
		// separately drives Feature.Execute on whichever signal they
		// want; profiler captures wherever Execute is called.
		public extern void AutoInitialize(Feature[] features);

		// Attach a profiler to features added after AutoInitialize —
		// useful when features are constructed lazily (e.g., entering a
		// level loads gameplay features that weren't around at startup).
		public extern void AddProfiledFeatures(Feature[] features);

		// Show / Hide / Toggle — client-only. F4 calls Toggle.
		public extern void Show();
		public extern void Hide();
		public extern void Toggle();

		public extern bool IsServerView();
		public extern void SwitchToServerView();
		public extern void SwitchToClientView();
	}
}
