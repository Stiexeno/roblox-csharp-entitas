using System.Collections.Generic;

namespace Entitas
{
	// Composite-pattern system runner. `Add()` accepts anything ISystem;
	// at runtime we bucket each system into the appropriate phase list by
	// concrete-interface cast. Calling Initialize / Execute / Cleanup /
	// TearDown walks the bucket in insertion order.
	public class Systems : IInitializeSystem, IExecuteSystem, ICleanupSystem, ITearDownSystem
	{
		protected readonly List<IInitializeSystem> _initializeSystems = new();
		protected readonly List<IExecuteSystem> _executeSystems = new();
		protected readonly List<ICleanupSystem> _cleanupSystems = new();
		protected readonly List<ITearDownSystem> _tearDownSystems = new();

		public virtual Systems Add(ISystem system)
		{
			if (system is IInitializeSystem initializeSystem) _initializeSystems.Add(initializeSystem);
			if (system is IExecuteSystem executeSystem) _executeSystems.Add(executeSystem);
			if (system is ICleanupSystem cleanupSystem) _cleanupSystems.Add(cleanupSystem);
			if (system is ITearDownSystem tearDownSystem) _tearDownSystems.Add(tearDownSystem);
			return this;
		}

		public virtual void Initialize()
		{
			for (int i = 0; i < _initializeSystems.Count; i++) _initializeSystems[i].Initialize();
		}

		public virtual void Execute()
		{
			for (int i = 0; i < _executeSystems.Count; i++) _executeSystems[i].Execute();
		}

		public virtual void Cleanup()
		{
			for (int i = 0; i < _cleanupSystems.Count; i++) _cleanupSystems[i].Cleanup();
		}

		public virtual void TearDown()
		{
			for (int i = 0; i < _tearDownSystems.Count; i++) _tearDownSystems[i].TearDown();
		}
	}
}
