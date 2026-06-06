#pragma warning disable CS0626 // Methods are implemented in runtime/Context.luau, not in this assembly.

namespace Entitas
{
	// IDE type-checking surface only — runtime/Context.luau is what
	// actually runs. Generated {Ctx}Context partials extend this and
	// override CreateEntityInstance so the runtime can produce a typed
	// entity without C#'s `new TEntity()` constraint (no Luau equivalent).
	public class Context<TEntity> : IContext<TEntity> where TEntity : class, IEntity, new()
	{
		public extern int totalComponents { get; }
		public extern int count { get; }

		public extern Context(int totalComponents);

		// Overridden by codegen-emitted {Ctx}Context to return the concrete
		// typed entity. Runtime's CreateEntity calls this via virtual dispatch.
		public virtual extern TEntity CreateEntityInstance();

		public extern TEntity CreateEntity();
		public extern void Initialize(TEntity entity);

		public extern bool HasEntity(TEntity entity);
		public extern TEntity[] GetEntities();

		public extern IGroup<TEntity> GetGroup(IMatcher<TEntity> matcher);

		public extern void DestroyAllEntities();
		public extern void ResetCreationIndex();
		public extern void Reset();

		// Hook the runtime fires after entity component mutations so cached
		// groups can re-check membership.
		public extern void NotifyComponentChanged(TEntity entity, int index, IComponent component);
	}
}
