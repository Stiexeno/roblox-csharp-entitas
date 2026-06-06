using System;

namespace Entitas.CodeGeneration.Attributes
{
	// Generates a singleton accessor on the context: instead of
	// `entity.AddX(value)` you get `context.SetX(value)` / `context.x`.
	// One-of-a-kind state (player pawn, current input, score, …) lives
	// here rather than being modeled as an entity with the component.
	[AttributeUsage(AttributeTargets.Class)]
	public class UniqueAttribute : Attribute
	{
	}
}
