namespace Entitas
{
	// Runs once when Systems.Initialize() fires. The conventional spot
	// for one-shot setup that depends on the whole context graph being
	// constructed but before any Execute tick.
	public interface IInitializeSystem : ISystem
	{
		void Initialize();
	}
}
