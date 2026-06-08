namespace Entities
{
	// Runs once when Systems.TearDown() fires. The mirror of
	// IInitializeSystem: cleanup that must happen before the context
	// itself goes away (writing save data, disconnecting handlers, etc.).
	public interface ITearDownSystem : ISystem
	{
		void TearDown();
	}
}
