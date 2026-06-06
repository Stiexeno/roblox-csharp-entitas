#pragma warning disable CS0626 // Methods are implemented in runtime/Entity.luau, not in this assembly.

namespace Entitas
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

		public extern void Initialize(int creationIndex, int totalComponents);
		public extern void Reactivate(int creationIndex);

		public extern void AddComponent(int index, IComponent component);
		public extern void RemoveComponent(int index);
		public extern void ReplaceComponent(int index, IComponent component);

		public extern IComponent GetComponent(int index);
		public extern IComponent[] GetComponents();
		public extern int[] GetComponentIndices();

		public extern bool HasComponent(int index);
		public extern bool HasComponents(int[] indices);
		public extern bool HasAnyComponent(int[] indices);

		public extern void RemoveAllComponents();

		// Pool-aware allocator — see IEntity.CreateComponent for semantics.
		public extern T CreateComponent<T>(int index) where T : new();

		public extern void Destroy();
	}
}
