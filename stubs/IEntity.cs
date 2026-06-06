namespace Entitas
{
	// Public Entitas-1.14 surface. The alpha runtime drops AERC retain /
	// release, event delegates, and component pools — frozen-feast-style
	// code doesn't reach for them. The TODOs in Entity.cs flag where the
	// production runtime needs to grow back.
	public interface IEntity
	{
		int totalComponents { get; }
		int creationIndex { get; }
		bool isEnabled { get; }

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

		void Destroy();
	}
}
