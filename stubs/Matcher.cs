#pragma warning disable CS0626 // Methods are implemented in runtime/Matcher.luau, not in this assembly.

namespace Entities
{
	// IDE type-checking surface — runtime/Matcher.luau is the real impl.
	// The fluent-builder shape is preserved through the I*Matcher interface
	// chain (AllOf → IAllOfMatcher → AnyOf → IAnyOfMatcher → NoneOf), so
	// user-side `GameMatcher.AllOf(...).AnyOf(...).NoneOf(...)` type-checks
	// even though nothing here executes.
	public class Matcher<TEntity> : IAllOfMatcher<TEntity>, IAnyOfMatcher<TEntity>, INoneOfMatcher<TEntity>
		where TEntity : class, IEntity
	{
		public extern int[] indices { get; }
		public extern int[] allOfIndices { get; }
		public extern int[] anyOfIndices { get; }
		public extern int[] noneOfIndices { get; }

		// Settable — codegen-emitted GameMatcher.Player assigns
		// `m.componentNames = GameComponentsLookup.componentNames` so the
		// runtime's debug ToString can name components.
		public extern string[] componentNames { get; set; }

		public static extern IAllOfMatcher<TEntity> AllOf(params int[] indices);
		public static extern IAllOfMatcher<TEntity> AllOf(params IMatcher<TEntity>[] matchers);

		public static extern IAnyOfMatcher<TEntity> AnyOfRoot(params int[] indices);
		public static extern IAnyOfMatcher<TEntity> AnyOfRoot(params IMatcher<TEntity>[] matchers);

		public extern IAnyOfMatcher<TEntity> AnyOf(params int[] indices);
		public extern IAnyOfMatcher<TEntity> AnyOf(params IMatcher<TEntity>[] matchers);

		public extern INoneOfMatcher<TEntity> NoneOf(params int[] indices);
		public extern INoneOfMatcher<TEntity> NoneOf(params IMatcher<TEntity>[] matchers);

		public extern bool Matches(TEntity entity);
	}

	// Non-generic static surface — the codegen routes through this so the
	// transpiler can emit `Matcher.AllOf(_T, ...)` (no GenericNameSyntax
	// LHS, which isn't lowered today). Type-arg <TEntity> erases at the
	// Luau level per the converter's generic-erasure convention.
	public static class Matcher
	{
		public static extern IAllOfMatcher<TEntity> AllOf<TEntity>(params IMatcher<TEntity>[] matchers)
			where TEntity : class, IEntity;

		public static extern IAllOfMatcher<TEntity> AllOfIndices<TEntity>(params int[] indices)
			where TEntity : class, IEntity;

		public static extern IAnyOfMatcher<TEntity> AnyOf<TEntity>(params IMatcher<TEntity>[] matchers)
			where TEntity : class, IEntity;
	}
}
