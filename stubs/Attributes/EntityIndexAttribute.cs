using System;

namespace Entities.CodeGeneration.Attributes
{
	// Field-level marker. Codegen emits a `Dictionary<TKey, HashSet<TEntity>>`
	// on the owning {Ctx}Context plus a `GetEntitiesWith{Component}(TKey)`
	// lookup. The entity's AddX / ReplaceX / RemoveX bodies tail-call the
	// context's _Register / _Unregister hooks to keep the dict in sync. Use
	// for many-entities-per-key indices (e.g. owner → list of owned items).
	[AttributeUsage(AttributeTargets.Field)]
	public class EntityIndexAttribute : Attribute
	{
	}
}
