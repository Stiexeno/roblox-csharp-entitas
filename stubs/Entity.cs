#pragma warning disable CS0626 // Methods are implemented in runtime/Entity.luau, not in this assembly.

namespace Entities
{
	// IDE type-checking surface only. Every member is `extern` to make the
	// "implemented elsewhere" intent explicit — the actual code lives in
	// runtime/Entity.luau, and the transpiler routes references to this
	// class through that module by filename (stubs ↔ runtime convention).
	//
	// Generated {Ctx}Entity partials inherit from this. The codegen's
	// per-component property + method emit calls into AddComponent /
	// HasComponent / etc., which match the names the Luau runtime exposes,
	// so dispatch lines up at runtime even though nothing here executes.
	public class Entity : IEntity
	{
		public extern int totalComponents { get; }
		public extern int creationIndex { get; }
		public extern bool isEnabled { get; }

		// Back-reference to the context that owns this entity. Set by
		// Context.Initialize on creation; read by the codegen-emitted
		// AddX/ReplaceX/RemoveX bodies for [Unique] singleton tracking and
		// [EntityIndex]/[PrimaryEntityIndex] dict maintenance. Untyped on
		// purpose — the codegen casts to the concrete {Ctx}Context when it
		// needs the typed surface.
		public extern IContext context { get; }

		public extern void Initialize(int creationIndex, int totalComponents);
		public extern void Reactivate(int creationIndex);

		public extern void AddComponent(int index, IComponent component);
		public extern void RemoveComponent(int index);
		public extern void ReplaceComponent(int index, IComponent component);

		public extern IComponent GetComponent(int index);

		// Frozen-snapshot variant: returns a shallow-cloned, table.frozen
		// copy of the component so user code can't mutate fields in place.
		// The codegen-emitted property getter for [Replicated] multi-field
		// components routes through this, closing the silent-desync hazard
		// where `e.Position.X = 5` would otherwise change state without
		// going through ReplaceX (which is the replication entrypoint).
		// Non-replicated components still hit GetComponent to avoid the
		// per-Get clone allocation.
		public extern IComponent GetReplicatedComponent(int index);

		public extern IComponent[] GetComponents();
		public extern int[] GetComponentIndices();

		public extern bool HasComponent(int index);
		public extern bool HasComponents(int[] indices);
		public extern bool HasAnyComponent(int[] indices);

		public extern void RemoveAllComponents();

		// Pool-aware allocator — see IEntity.CreateComponent for semantics.
		public extern T CreateComponent<T>(int index) where T : new();

		// Virtual so the codegen-emitted {Ctx}Entity can override and
		// pre-fire RemoveX (or `IsX = false`) for every hooked component
		// before the base teardown runs — keeping replication ops, [Unique]
		// singleton fields, and [EntityIndex] dicts in sync when the user
		// destroys an entity directly instead of going through UnsetX /
		// RemoveX.
		public virtual extern void Destroy();
	}
}
