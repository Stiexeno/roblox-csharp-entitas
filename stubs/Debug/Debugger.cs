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

		// Wires the primary feature group to a signal (usually
		// RunService.Heartbeat). Creates the ScreenGui, binds F4, and
		// drives plasma frames.
		public extern void AutoInitialize(object signal, Feature[] features);

		// For games that split features across multiple signals
		// (Heartbeat + RenderStepped). Profiles the extra features on
		// their own signal; only AutoInitialize's signal drives the UI.
		public extern void AddFeatureGroup(object signal, Feature[] features);

		// Show / Hide / Toggle — client-only. F4 calls Toggle.
		public extern void Show();
		public extern void Hide();
		public extern void Toggle();

		public extern bool IsServerView();
		public extern void SwitchToServerView();
		public extern void SwitchToClientView();
	}
}
