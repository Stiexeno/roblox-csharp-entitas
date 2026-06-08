using Microsoft.CodeAnalysis;

namespace RobloxCSharp.Extensions.Entities
{
	// Bundle of attribute symbols looked up once from the Roslyn
	// compilation. Passed to ComponentModel so it doesn't have to re-query
	// per field; also lets PreSourceDiscovery share the same lookups for
	// scanning [PostConstructor] methods on partial Contexts.
	internal sealed class AttributeSymbols
	{
		public INamedTypeSymbol Component;
		public INamedTypeSymbol Context;
		public INamedTypeSymbol Replicated;
		public INamedTypeSymbol Unique;
		public INamedTypeSymbol EntityIndex;
		public INamedTypeSymbol PrimaryEntityIndex;
		public INamedTypeSymbol PostConstructor;
		public INamedTypeSymbol Watched;

		public static bool HasAttribute(ISymbol symbol, INamedTypeSymbol attr)
		{
			if (attr is null || symbol is null) return false;
			foreach (AttributeData a in symbol.GetAttributes())
			{
				if (SymbolEqualityComparer.Default.Equals(a.AttributeClass, attr)) return true;
			}
			return false;
		}
	}
}
