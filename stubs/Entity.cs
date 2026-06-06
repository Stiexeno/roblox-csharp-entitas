using System;
using System.Collections.Generic;

namespace Entitas
{
	// Simplified port of Entitas-1.14's Entity. The alpha runtime drops:
	//   - AERC retain / release (Roblox is GC'd; no manual lifetime mgmt)
	//   - Event delegates (OnComponentAdded, …)
	//   - Component pools (Lua tables churn cheaply enough for now)
	//   - ContextInfo / componentNames lookups
	// Generated GameEntity / InputEntity / … inherit from this and the
	// codegen adds typed extension methods (isPlayer, AddHealth, …) over
	// the dictionary-backed AddComponent / HasComponent / etc.
	//
	// TODO: re-introduce component pools + event delegates when the
	// runtime is hardened; frozen-feast doesn't lean on them but they're
	// part of the documented Entitas contract.
	public class Entity : IEntity
	{
		private readonly Dictionary<int, IComponent> _components = new();

		public int totalComponents { get; private set; }
		public int creationIndex { get; private set; }
		public bool isEnabled { get; private set; }

		public void Initialize(int creationIndex, int totalComponents)
		{
			this.creationIndex = creationIndex;
			this.totalComponents = totalComponents;
			isEnabled = true;
		}

		public void Reactivate(int creationIndex)
		{
			this.creationIndex = creationIndex;
			isEnabled = true;
		}

		public void AddComponent(int index, IComponent component)
		{
			if (!isEnabled) throw new InvalidOperationException("Cannot add component to a destroyed entity.");
			if (_components.ContainsKey(index))
				throw new InvalidOperationException($"Entity already has component at index {index}.");
			_components[index] = component;
		}

		public void RemoveComponent(int index)
		{
			if (!isEnabled) throw new InvalidOperationException("Cannot remove component from a destroyed entity.");
			if (!_components.ContainsKey(index))
				throw new InvalidOperationException($"Entity does not have component at index {index}.");
			_components.Remove(index);
		}

		public void ReplaceComponent(int index, IComponent component)
		{
			if (!isEnabled) throw new InvalidOperationException("Cannot replace component on a destroyed entity.");
			if (component is null)
			{
				if (_components.ContainsKey(index)) _components.Remove(index);
				return;
			}
			_components[index] = component;
		}

		public IComponent GetComponent(int index)
		{
			if (!_components.TryGetValue(index, out IComponent component))
				throw new InvalidOperationException($"Entity does not have component at index {index}.");
			return component;
		}

		public IComponent[] GetComponents()
		{
			IComponent[] result = new IComponent[_components.Count];
			int i = 0;
			foreach (KeyValuePair<int, IComponent> kvp in _components) result[i++] = kvp.Value;
			return result;
		}

		public int[] GetComponentIndices()
		{
			int[] result = new int[_components.Count];
			int i = 0;
			foreach (KeyValuePair<int, IComponent> kvp in _components) result[i++] = kvp.Key;
			return result;
		}

		public bool HasComponent(int index) => _components.ContainsKey(index);

		public bool HasComponents(int[] indices)
		{
			for (int i = 0; i < indices.Length; i++)
				if (!_components.ContainsKey(indices[i])) return false;
			return true;
		}

		public bool HasAnyComponent(int[] indices)
		{
			for (int i = 0; i < indices.Length; i++)
				if (_components.ContainsKey(indices[i])) return true;
			return false;
		}

		public void RemoveAllComponents()
		{
			_components.Clear();
		}

		public void Destroy()
		{
			RemoveAllComponents();
			isEnabled = false;
		}
	}
}
