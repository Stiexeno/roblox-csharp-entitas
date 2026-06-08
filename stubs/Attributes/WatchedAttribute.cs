using System;

namespace Entities.CodeGeneration.Attributes
{
	// Class-level marker on an IComponent. Codegen synthesizes a paired
	// `{Name}Changed` flag component in the same context + namespace, and
	// patches every state-mutating entity body (AddX / ReplaceX / unwrap
	// setter; flag setter in either direction) to also set
	// `Is{Name}Changed = true`. RemoveX deliberately does NOT set the
	// flag — the entity no longer has the component, so the Changed
	// matcher couldn't observe it.
	//
	// The Changed flag is local-only — it never goes on the [Replicated]
	// wire. Each runtime side (server, client) raises its own Changed in
	// response to its own mutations, so [Watched] + [Replicated] compose:
	// the client's `Apply{Name}` calls `Replace{Name}` which sets
	// `Is{Name}Changed` locally for client-side reactive systems.
	//
	// Cleanup: codegen emits a `{Ctx}WatchedCleanupSystem : ICleanupSystem`
	// when any component in the context is [Watched]. The user adds it to
	// the tail of the feature pipeline; it clears every Changed flag at
	// end of frame so reactive systems see one-frame signals.
	[AttributeUsage(AttributeTargets.Class)]
	public class WatchedAttribute : Attribute
	{
	}
}
