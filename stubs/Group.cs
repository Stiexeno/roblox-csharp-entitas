using System.Collections.Generic;

namespace Entitas
{
	// Simplified port of Entitas-1.14's Group. Drops event delegates and
	// the GroupSingleEntityException nuance (the alpha throws plain
	// InvalidOperationException from GetSingleEntity instead).
	public class Group<TEntity> : IGroup<TEntity> where TEntity : class, IEntity
	{
		private readonly HashSet<TEntity> _entities = new();
		private TEntity[] _entitiesCache;
		private TEntity _singleEntityCache;

		public IMatcher<TEntity> matcher { get; }

		public int count => _entities.Count;

		public Group(IMatcher<TEntity> matcher)
		{
			this.matcher = matcher;
		}

		public void HandleEntitySilently(TEntity entity)
		{
			if (matcher.Matches(entity))
			{
				if (_entities.Add(entity)) InvalidateCaches();
			}
			else
			{
				if (_entities.Remove(entity)) InvalidateCaches();
			}
		}

		public void HandleEntity(TEntity entity, int index, IComponent component)
			=> HandleEntitySilently(entity);

		public bool ContainsEntity(TEntity entity) => _entities.Contains(entity);

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

		public List<TEntity> GetEntities(List<TEntity> buffer)
		{
			buffer.Clear();
			foreach (TEntity e in _entities) buffer.Add(e);
			return buffer;
		}

		public TEntity GetSingleEntity()
		{
			if (_entities.Count != 1)
				throw new System.InvalidOperationException(
					$"Group expected exactly one entity but contains {_entities.Count}.");
			if (_singleEntityCache is null)
			{
				foreach (TEntity e in _entities) { _singleEntityCache = e; break; }
			}
			return _singleEntityCache;
		}

		public IEnumerable<TEntity> AsEnumerable() => _entities;

		private void InvalidateCaches()
		{
			_entitiesCache = null;
			_singleEntityCache = null;
		}
	}
}
