#pragma warning disable CS0626 // Methods are implemented in runtime/CommandHandlerSystem.luau, not in this assembly.

namespace Entities
{
	// Base class for server-side systems that handle a single command
	// kind. Subclass and override OnExecute to process the matched
	// entities; the base auto-destroys every entity in the group after
	// OnExecute returns (set ShouldCancelRequestDeletion = true inside
	// OnExecute to opt out for an edge case where you need the entity to
	// live another frame).
	//
	// Mirrors frozen-feast's RequestHandlerSystem shape — same generic
	// over TEntity, same pre-built group injected at construction, same
	// reusable buffer for the destroy loop. Pair with [Command]
	// components: the server's CommandReceiver spawns the entity, this
	// base picks it up via the group, your OnExecute validates and acts,
	// the base cleans up.
	public abstract class CommandHandlerSystem<TEntity> : IExecuteSystem
		where TEntity : class, IEntity
	{
		protected extern CommandHandlerSystem(IGroup<TEntity> requestGroup);

		protected extern bool ShouldCancelRequestDeletion { get; set; }

		public extern void Execute();

		protected abstract void OnExecute(IGroup<TEntity> requests);
	}
}
