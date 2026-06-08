namespace Entities
{
	// Runs every Systems.Execute() tick. The workhorse — most game logic
	// lives in IExecuteSystem implementations that read from groups,
	// mutate entities, and emit events.
	public interface IExecuteSystem : ISystem
	{
		void Execute();
	}
}
