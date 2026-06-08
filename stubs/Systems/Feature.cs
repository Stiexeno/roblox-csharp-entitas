#pragma warning disable CS0626 // Methods are implemented in runtime/Feature.luau, not in this assembly.

namespace Entities
{
	// IDE type-checking surface — runtime/Feature.luau is the real impl.
	public class Feature : Systems
	{
		public extern string name { get; }

		public extern Feature(string name);
		public extern Feature();
	}
}
