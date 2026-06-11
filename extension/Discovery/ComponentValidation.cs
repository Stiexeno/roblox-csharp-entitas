using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using RobloxCSharp.Common.Diagnostics;
using RobloxCSharp.Transformer.Symbols;

namespace RobloxCSharp.Extensions.Entities
{
	// Codegen-time guards for component shapes that would otherwise
	// compile fine and fail at runtime (or silently generate nothing).
	internal static class ComponentValidation
	{
		// EntitiesReplication.QueueSet has overloads for 0–6 fields and the
		// delta bitmask is budgeted accordingly; a 7th field compiles but
		// hits a missing overload at runtime.
		private const int MaxReplicatedFields = 6;

		public static void Run(Dictionary<string, ContextModel> byContext, DiagnosticBag diagnostics)
		{
			foreach (ContextModel ctx in byContext.Values)
			{
				foreach (ComponentModel component in ctx.Components)
				{
					if (component.Symbol is null) continue;

					if (component.IsReplicated && component.Fields.Count > MaxReplicatedFields)
					{
						diagnostics.Error("ENT0001",
							$"[Replicated] component '{component.TypeName}' has {component.Fields.Count} public fields; "
							+ $"the replication wire supports at most {MaxReplicatedFields}. "
							+ "Split it into smaller components or drop [Replicated].",
							Location(component.Symbol));
					}

					if (HasPublicInstanceProperty(component.Symbol))
					{
						diagnostics.Warning("ENT0002",
							$"component '{component.TypeName}' declares public properties, which codegen ignores — "
							+ "only public instance fields generate accessors. Use fields.",
							Location(component.Symbol));
					}
				}
			}
		}

		private static bool HasPublicInstanceProperty(INamedTypeSymbol symbol)
		{
			foreach (ISymbol member in symbol.GetMembers())
			{
				if (member is IPropertySymbol { DeclaredAccessibility: Accessibility.Public, IsStatic: false })
					return true;
			}
			return false;
		}

		private static DiagnosticLocation Location(INamedTypeSymbol symbol)
		{
			if (symbol.DeclaringSyntaxReferences.Length == 0) return DiagnosticLocation.Unknown;
			return symbol.DeclaringSyntaxReferences[0].GetSyntax().ToDiagnosticLocation();
		}
	}
}
