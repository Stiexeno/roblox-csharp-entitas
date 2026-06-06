namespace Entitas
{
	// Public Entitas-1.14 surface. Alpha runtime drops AERC retain /
	// release and event delegates; component pools land in a separate
	// commit. Entity pool is wired here.
	public interface IEntity
	{
		int totalComponents { get; }
		int creationIndex { get; }
		bool isEnabled { get; }

		// Wires a fresh entity for use — called by Context.CreateEntity
		// right after the concrete instance is constructed.
		void Initialize(int creationIndex, int totalComponents);

		// Re-wires a recycled entity pulled off Context._reusableEntities.
		// Distinct from Initialize so totalComponents / pool refs don't
		// have to be re-set; only the creationIndex changes per generation.
		void Reactivate(int creationIndex);

		void AddComponent(int index, IComponent component);
		void RemoveComponent(int index);
		void ReplaceComponent(int index, IComponent component);

		IComponent GetComponent(int index);
		IComponent[] GetComponents();
		int[] GetComponentIndices();

		bool HasComponent(int index);
		bool HasComponents(int[] indices);
		bool HasAnyComponent(int[] indices);

		void RemoveAllComponents();

		// Pool-aware component allocator. The codegen-emitted AddX /
		// ReplaceX methods route through this so removed/replaced
		// components recycle through Context._componentPools instead of
		// churning the GC. Falls back to T.new() when the pool is empty.
		T CreateComponent<T>(int index) where T : new();

		void Destroy();
	}
}
