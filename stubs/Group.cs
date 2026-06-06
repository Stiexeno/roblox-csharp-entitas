#pragma warning disable CS0626 // Methods are implemented in runtime/Group.luau, not in this assembly.

using System.Collections.Generic;

namespace Entitas
{
	// IDE type-checking surface — runtime/Group.luau is the real impl.
	public class Group<TEntity> : IGroup<TEntity> where TEntity : class, IEntity
	{
		public extern IMatcher<TEntity> matcher { get; }
		public extern int count { get; }

		public extern Group(IMatcher<TEntity> matcher);

		public extern void HandleEntitySilently(TEntity entity);
		public extern void HandleEntity(TEntity entity, int index, IComponent component);

		public extern bool ContainsEntity(TEntity entity);

		public extern TEntity[] GetEntities();
		public extern List<TEntity> GetEntities(List<TEntity> buffer);
		public extern TEntity GetSingleEntity();

		public extern IEnumerable<TEntity> AsEnumerable();
	}
}
