using System;

namespace Entities.CodeGeneration.Attributes
{
	// Field-level marker. Codegen emits a `Dictionary<TKey, TEntity>` on
	// the owning {Ctx}Context plus a `GetEntityWith{Component}(TKey)`
	// lookup that returns the single entity (or null) for a key. Use for
	// unique-per-key indices (e.g. userId → user entity). The dict is
	// kept in sync by the entity's AddX / ReplaceX / RemoveX hooks; a
	// duplicate insert throws on the dict's `Add`, so two entities
	// claiming the same primary key is a hard error by design.
	[AttributeUsage(AttributeTargets.Field)]
	public class PrimaryEntityIndexAttribute : Attribute
	{
	}
}
