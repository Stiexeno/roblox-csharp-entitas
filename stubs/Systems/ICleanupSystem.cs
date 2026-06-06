namespace Entitas
{
	// Runs every Systems.Cleanup() tick, after Execute. The conventional
	// spot for end-of-frame work: clearing one-frame flags, destroying
	// entities that got marked this tick, etc.
	public interface ICleanupSystem : ISystem
	{
		void Cleanup();
	}
}
