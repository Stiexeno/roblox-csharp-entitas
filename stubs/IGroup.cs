using System.Collections.Generic;

namespace Entitas
{
	public interface IGroup
	{
		int count { get; }
	}

	// Typed group — what `context.GetGroup(matcher)` returns. The buffer
	// overload (`GetEntities(List<TEntity>)`) is the perf-aware variant
	// frozen-feast uses everywhere; mirrors Entitas-1.14 verbatim.
	public interface IGroup<TEntity> : IGroup where TEntity : class, IEntity
	{
		IMatcher<TEntity> matcher { get; }

		bool ContainsEntity(TEntity entity);

		TEntity[] GetEntities();
		List<TEntity> GetEntities(List<TEntity> buffer);
		TEntity GetSingleEntity();

		IEnumerable<TEntity> AsEnumerable();

		// Internal hooks the context calls when an entity gains / loses
		// a component the matcher cares about. Exposed on the interface
		// because the codegen-side Context implementation routes through
		// it directly.
		void HandleEntitySilently(TEntity entity);
		void HandleEntity(TEntity entity, int index, IComponent component);
	}
}
