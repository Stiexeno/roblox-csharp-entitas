using System;

namespace Entities.CodeGeneration.Attributes
{
	// Base marker for context-assignment attributes. Subclass per context
	// name ([Game], [Input], [Inventory], …) and apply to IComponent
	// types to route them into a specific context's lookup. The codegen
	// reads the subclass's type name (minus "Attribute") to discover
	// which contexts exist and which components belong where.
	[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
	public class ContextAttribute : Attribute
	{
		public readonly string contextName;

		public ContextAttribute(string contextName)
		{
			this.contextName = contextName;
		}
	}
}
