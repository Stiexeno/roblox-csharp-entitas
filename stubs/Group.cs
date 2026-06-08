#pragma warning disable CS0626 // Methods are implemented in runtime/Group.luau, not in this assembly.

using System.Collections;
using System.Collections.Generic;

namespace Entities
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

		// IEnumerable<T> contract — needed so `foreach (var e in group)`
		// type-checks. Lua-side iteration is wired via __iter on the Group
		// metatable; this enumerator is the C# surface only.
		public extern IEnumerator<TEntity> GetEnumerator();
		extern IEnumerator IEnumerable.GetEnumerator();
	}
}
