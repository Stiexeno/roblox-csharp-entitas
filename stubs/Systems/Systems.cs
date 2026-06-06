#pragma warning disable CS0626 // Methods are implemented in runtime/Systems.luau, not in this assembly.

namespace Entitas
{
	// IDE type-checking surface — runtime/Systems.luau is the real impl.
	// Implements every system-phase interface so Add(this) self-bucketing
	// works in user code (frozen-feast nests features inside features).
	public class Systems : IInitializeSystem, IExecuteSystem, ICleanupSystem, ITearDownSystem
	{
		public virtual extern Systems Add(ISystem system);

		public virtual extern void Initialize();
		public virtual extern void Execute();
		public virtual extern void Cleanup();
		public virtual extern void TearDown();
	}
}
