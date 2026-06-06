using System;
using System.Collections.Generic;

namespace Entitas
{
	// Simplified port of Entitas-1.14's Context<TEntity>. Drops:
	//   - reusable-entity pool (entity instances are GC'd, not pooled)
	//   - event delegates (OnEntityCreated, …)
	//   - retained-entity tracking
	//   - AERC
	//   - ComponentInfo / pools
	// Generated GameContext extends this and passes (totalComponents,
	// componentNames, componentTypes, entityFactory).
	//
	// TODO: re-introduce entity pooling + event delegates when the
	// runtime is hardened.
	public class Context<TEntity> : IContext<TEntity> where TEntity : class, IEntity, new()
	{
		private readonly HashSet<TEntity> _entities = new();
		private readonly Dictionary<IMatcher<TEntity>, Group<TEntity>> _groups = new();
		private TEntity[] _entitiesCache;
		private int _creationIndex;

		public int totalComponents { get; }
		public int count => _entities.Count;

		public Context(int totalComponents)
		{
			this.totalComponents = totalComponents;
		}

		// Codegen emits an override that constructs the concrete entity
		// type. Lua has no generic-constraint `new TEntity()` so the
		// subclass has to hand back the instance explicitly.
		public virtual TEntity CreateEntityInstance() { return new(); }

		public TEntity CreateEntity()
		{
			TEntity entity = CreateEntityInstance();
			entity.Initialize(_creationIndex++, totalComponents);
			_entities.Add(entity);
			_entitiesCache = null;
			return entity;
		}

		public bool HasEntity(TEntity entity) => _entities.Contains(entity);

		public TEntity[] GetEntities()
		{
			if (_entitiesCache is null)
			{
				_entitiesCache = new TEntity[_entities.Count];
				int i = 0;
				foreach (TEntity e in _entities) _entitiesCache[i++] = e;
			}
			return _entitiesCache;
		}

		public IGroup<TEntity> GetGroup(IMatcher<TEntity> matcher)
		{
			if (!_groups.TryGetValue(matcher, out Group<TEntity> group))
			{
				group = new Group<TEntity>(matcher);
				_groups[matcher] = group;
				foreach (TEntity e in _entities) group.HandleEntitySilently(e);
			}
			return group;
		}

		public void DestroyAllEntities()
		{
			foreach (TEntity e in _entities) e.Destroy();
			_entities.Clear();
			_entitiesCache = null;
		}

		public void ResetCreationIndex() => _creationIndex = 0;

		public void Reset()
		{
			DestroyAllEntities();
			ResetCreationIndex();
		}

		// Called by the generated entity extension methods (Add / Replace
		// / Remove) after they mutate the underlying entity so any cached
		// groups can re-check membership.
		public void NotifyComponentChanged(TEntity entity, int index, IComponent component)
		{
			foreach (KeyValuePair<IMatcher<TEntity>, Group<TEntity>> kvp in _groups)
				kvp.Value.HandleEntity(entity, index, component);
		}

		// Initialize / Initialize for a brand-new entity that came in via
		// a path other than CreateEntity (e.g. unit tests). Public so the
		// codegen-side context can use it.
		public void Initialize(TEntity entity)
		{
			entity.Initialize(_creationIndex++, totalComponents);
			_entities.Add(entity);
			_entitiesCache = null;
		}
	}

	// Untyped `Contexts.allContexts` walk — generated Contexts.cs returns
	// an array of IContext, but the items are concrete Context<T>.
	public static class ContextExtensions
	{
		public static void ResetAll(this IContext[] contexts)
		{
			for (int i = 0; i < contexts.Length; i++) contexts[i].Reset();
		}
	}
}
