using System;
using System.Collections.Generic;

namespace Entitas
{
	// Single shared compound matcher used for both leaf (single-index)
	// and fluent-built matchers. The codegen's `GameMatcher.Player`
	// returns one of these with `allOfIndices = [PlayerIndex]`; chains
	// like `.AnyOf(...).NoneOf(...)` mutate-then-return-self to grow it.
	public class Matcher<TEntity> : IAllOfMatcher<TEntity>, IAnyOfMatcher<TEntity>, INoneOfMatcher<TEntity>
		where TEntity : class, IEntity
	{
		public int[] indices
		{
			get
			{
				if (_indices is null) _indices = MergeIndices();
				return _indices;
			}
		}

		public int[] allOfIndices { get; private set; }
		public int[] anyOfIndices { get; private set; }
		public int[] noneOfIndices { get; private set; }

		// Used by codegen for ToString / debugging. Maps slot index to
		// component type name. The codegen sets it on the leaf matcher.
		public string[] componentNames { get; set; }

		private int[] _indices;

		public static IAllOfMatcher<TEntity> AllOf(params int[] indices)
		{
			Matcher<TEntity> matcher = new() { allOfIndices = Distinct(indices) };
			return matcher;
		}

		public static IAllOfMatcher<TEntity> AllOf(params IMatcher<TEntity>[] matchers)
			=> AllOf(MergeIndicesFromMatchers(matchers));

		public static IAnyOfMatcher<TEntity> AnyOfRoot(params int[] indices)
		{
			Matcher<TEntity> matcher = new() { anyOfIndices = Distinct(indices) };
			return matcher;
		}

		public IAnyOfMatcher<TEntity> AnyOf(params int[] indices)
		{
			anyOfIndices = Distinct(indices);
			_indices = null;
			return this;
		}

		public IAnyOfMatcher<TEntity> AnyOf(params IMatcher<TEntity>[] matchers)
			=> AnyOf(MergeIndicesFromMatchers(matchers));

		public INoneOfMatcher<TEntity> NoneOf(params int[] indices)
		{
			noneOfIndices = Distinct(indices);
			_indices = null;
			return this;
		}

		public INoneOfMatcher<TEntity> NoneOf(params IMatcher<TEntity>[] matchers)
			=> NoneOf(MergeIndicesFromMatchers(matchers));

		public bool Matches(TEntity entity)
		{
			bool matchesAllOf = allOfIndices is null || entity.HasComponents(allOfIndices);
			bool matchesAnyOf = anyOfIndices is null || entity.HasAnyComponent(anyOfIndices);
			bool matchesNoneOf = noneOfIndices is null || !entity.HasAnyComponent(noneOfIndices);
			return matchesAllOf && matchesAnyOf && matchesNoneOf;
		}

		private int[] MergeIndices()
		{
			int count = (allOfIndices?.Length ?? 0)
				+ (anyOfIndices?.Length ?? 0)
				+ (noneOfIndices?.Length ?? 0);
			int[] merged = new int[count];
			int i = 0;
			if (allOfIndices is not null) for (int j = 0; j < allOfIndices.Length; j++) merged[i++] = allOfIndices[j];
			if (anyOfIndices is not null) for (int j = 0; j < anyOfIndices.Length; j++) merged[i++] = anyOfIndices[j];
			if (noneOfIndices is not null) for (int j = 0; j < noneOfIndices.Length; j++) merged[i++] = noneOfIndices[j];
			return Distinct(merged);
		}

		private static int[] Distinct(int[] indices)
		{
			HashSet<int> seen = new();
			List<int> result = new();
			for (int i = 0; i < indices.Length; i++)
			{
				if (seen.Add(indices[i])) result.Add(indices[i]);
			}
			result.Sort();
			return result.ToArray();
		}

		private static int[] MergeIndicesFromMatchers(IMatcher<TEntity>[] matchers)
		{
			List<int> merged = new();
			for (int i = 0; i < matchers.Length; i++)
			{
				int[] indices = matchers[i].indices;
				for (int j = 0; j < indices.Length; j++) merged.Add(indices[j]);
			}
			return merged.ToArray();
		}
	}

	// Non-generic static surface — the codegen routes through this so the
	// emitted Luau gets `Matcher.AllOf(...)` (a flat function on the
	// non-generic Matcher import) instead of `Matcher<T>.AllOf(...)`
	// (which the transpiler can't lower today: GenericNameSyntax as a
	// type qualifier on a static-member access isn't implemented).
	//
	// Type arg <TEntity> erases at the Lua level, so the codegen's
	// `Matcher.AllOf<GameEntity>(matchers)` becomes `Matcher.AllOf(matchers)`
	// — exactly what the runtime/Matcher.luau exposes.
	public static class Matcher
	{
		public static IAllOfMatcher<TEntity> AllOf<TEntity>(params IMatcher<TEntity>[] matchers)
			where TEntity : class, IEntity
			=> Matcher<TEntity>.AllOf(matchers);

		// Internal-use only — the codegen's per-component getters
		// (`GameMatcher.Player`) need a single-index AllOf seed.
		public static IAllOfMatcher<TEntity> AllOfIndices<TEntity>(params int[] indices)
			where TEntity : class, IEntity
			=> Matcher<TEntity>.AllOf(indices);

		public static IAnyOfMatcher<TEntity> AnyOf<TEntity>(params IMatcher<TEntity>[] matchers)
			where TEntity : class, IEntity
			=> Matcher<TEntity>.AnyOf(matchers);
	}
}
