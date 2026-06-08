namespace Entities
{
	// Untyped context surface — the codegen wires every generated context
	// behind this so `IContexts.allContexts` returns IContext[] uniformly.
	public interface IContext
	{
		int totalComponents { get; }

		int count { get; }

		// How many entities are sitting on the reuse stack, ready to be
		// recycled by the next CreateEntity. Bumped on Destroy, drained
		// on CreateEntity. Useful for tuning + telemetry.
		int reusableEntitiesCount { get; }

		void DestroyAllEntities();

		void ResetCreationIndex();
		void Reset();
	}

	// Typed context — what user code holds (GameContext extends
	// Context<GameEntity>, the codegen generates the entity-typed
	// surface).
	public interface IContext<TEntity> : IContext where TEntity : class, IEntity
	{
		TEntity CreateEntity();

		bool HasEntity(TEntity entity);
		TEntity[] GetEntities();

		IGroup<TEntity> GetGroup(IMatcher<TEntity> matcher);
	}
}
