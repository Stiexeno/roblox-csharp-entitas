using System;

namespace Entitas.CodeGeneration.Attributes
{
	// Method-level marker. Methods carrying it on partial Contexts run
	// after the generated constructor wires every context up. Used to
	// register entity indices (see frozen-feast's Generated/Contexts.cs
	// InitializeEntityIndices).
	[AttributeUsage(AttributeTargets.Method)]
	public class PostConstructorAttribute : Attribute
	{
	}
}
