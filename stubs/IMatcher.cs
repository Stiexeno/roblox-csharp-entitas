namespace Entitas
{
	public interface IMatcher<TEntity> where TEntity : class, IEntity
	{
		int[] indices { get; }
		bool Matches(TEntity entity);
	}

	// Fluent-builder chain: `AllOf(...)` returns IAllOfMatcher, which lets
	// you chain `.AnyOf(...)` to get IAnyOfMatcher, which lets you chain
	// `.NoneOf(...)` to get the terminal INoneOfMatcher. Order matters —
	// you can't go back from `.AnyOf(...)` to add more AllOf indices.
	public interface ICompoundMatcher<TEntity> : IMatcher<TEntity> where TEntity : class, IEntity
	{
		int[] allOfIndices { get; }
		int[] anyOfIndices { get; }
		int[] noneOfIndices { get; }
	}

	public interface INoneOfMatcher<TEntity> : ICompoundMatcher<TEntity> where TEntity : class, IEntity { }

	public interface IAnyOfMatcher<TEntity> : INoneOfMatcher<TEntity> where TEntity : class, IEntity
	{
		INoneOfMatcher<TEntity> NoneOf(params int[] indices);
		INoneOfMatcher<TEntity> NoneOf(params IMatcher<TEntity>[] matchers);
	}

	public interface IAllOfMatcher<TEntity> : IAnyOfMatcher<TEntity> where TEntity : class, IEntity
	{
		IAnyOfMatcher<TEntity> AnyOf(params int[] indices);
		IAnyOfMatcher<TEntity> AnyOf(params IMatcher<TEntity>[] matchers);
	}
}
