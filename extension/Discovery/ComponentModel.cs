using System.Collections.Generic;
using Microsoft.CodeAnalysis;

namespace RobloxCSharp.Extensions.Entities
{
	internal sealed class ComponentModel
	{
		public INamedTypeSymbol Symbol { get; }
		public string TypeName { get; }
		public string FullName { get; }
		public string NamespaceName { get; }
		public List<ComponentField> Fields { get; }
		public bool IsFlag => Fields.Count == 0;
		public bool IsReplicated { get; }
		public bool IsUnique { get; }
		public bool IsWatched { get; }
		// True for the synthesized {X}Changed flag we auto-generate for
		// every [Watched] component. Codegen needs to know so it can skip
		// emitting hook code on the Changed itself (no Changed-of-Changed).
		public bool IsSynthesizedChangedFlag { get; }
		// True for the synthesized OriginUserId component injected into
		// every context (server-attached marker carrying the originating
		// Player.UserId for command entities; server-only).
		public bool IsSynthesizedOriginUserId { get; }
		// True for the synthesized Command flag injected into every
		// context. Its setter's true branch is the client→server ship
		// trigger; the flag also lives on the server-side spawned entity
		// so server systems can query AllOf<Command, ...> uniformly.
		public bool IsSynthesizedCommandFlag { get; }
		public bool HasIndexedField
		{
			get
			{
				for (int i = 0; i < Fields.Count; i++) if (Fields[i].IsIndexed) return true;
				return false;
			}
		}

		public ComponentModel(INamedTypeSymbol symbol, AttributeSymbols attrs)
		{
			Symbol = symbol;
			TypeName = symbol.Name;
			IsReplicated = AttributeSymbols.HasAttribute(symbol, attrs.Replicated);
			IsUnique = AttributeSymbols.HasAttribute(symbol, attrs.Unique);
			IsWatched = AttributeSymbols.HasAttribute(symbol, attrs.Watched);
			IsSynthesizedChangedFlag = false;
			IsSynthesizedOriginUserId = false;
			IsSynthesizedCommandFlag = false;
			FullName = symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
			NamespaceName = symbol.ContainingNamespace?.IsGlobalNamespace == false
				? symbol.ContainingNamespace.ToDisplayString()
				: null;
			Fields = new List<ComponentField>();
			foreach (ISymbol member in symbol.GetMembers())
			{
				if (member is IFieldSymbol field && field.DeclaredAccessibility == Accessibility.Public && !field.IsStatic && !field.IsConst)
				{
					FieldIndexKind kind = FieldIndexKind.None;
					if (AttributeSymbols.HasAttribute(field, attrs.PrimaryEntityIndex)) kind = FieldIndexKind.PrimaryIndex;
					else if (AttributeSymbols.HasAttribute(field, attrs.EntityIndex)) kind = FieldIndexKind.Index;
					Fields.Add(new ComponentField(field.Name, field.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), kind));
				}
				// Property-shaped values (`public int Value { get; set; }`) are
				// out of scope — frozen-feast uses plain fields everywhere — so
				// we keep this strict for the alpha and flag it for follow-up
				// if a user reports needing them.
			}
		}

		// Synthesized {Name}Changed flag for a [Watched] component. No
		// Roslyn symbol exists — codegen also emits the C# class file
		// (in PreSourceDiscovery) so user code can reference the type.
		// The synthesized model carries no attributes (no Replicated,
		// Unique, etc.) and is a pure flag; its only job is to be a
		// marker that the cleanup system can find.
		private ComponentModel(ComponentModel source)
		{
			Symbol = null;
			TypeName = source.TypeName + "Changed";
			NamespaceName = source.NamespaceName;
			FullName = source.NamespaceName is null
				? "global::" + TypeName
				: "global::" + source.NamespaceName + "." + TypeName;
			Fields = new List<ComponentField>();
			IsReplicated = false;
			IsUnique = false;
			IsWatched = false;
			IsSynthesizedChangedFlag = true;
			IsSynthesizedOriginUserId = false;
			IsSynthesizedCommandFlag = false;
		}

		public static ComponentModel CreateChangedFlag(ComponentModel source) => new(source);

		// Synthesized OriginUserId — server-attached marker carrying the
		// originating Player.UserId. Injected into every context so
		// server systems can validate command origin via
		// `AllOf<Command, OriginUserId>` regardless of which context
		// owns the command entity.
		public static ComponentModel CreateOriginUserId() => new(originUserIdMarker: true);

		private ComponentModel(bool originUserIdMarker)
		{
			Symbol = null;
			TypeName = "OriginUserId";
			NamespaceName = null;
			FullName = "global::OriginUserId";
			Fields = new List<ComponentField>
			{
				new ComponentField("Value", "long", FieldIndexKind.None),
			};
			IsReplicated = false;
			IsUnique = false;
			IsWatched = false;
			IsSynthesizedChangedFlag = false;
			IsSynthesizedOriginUserId = true;
			IsSynthesizedCommandFlag = false;
		}

		// Synthesized Command flag — the client→server ship trigger.
		// Setter's true-branch tail enqueues the entity for the next
		// heartbeat's command drain; the server-side spawned entity
		// also carries this flag so AllOf<Command, ...> works on both
		// sides. One per context; injected unconditionally.
		public static ComponentModel CreateCommandFlag() => new(commandFlagMarker: true, dummy: 0);

		private ComponentModel(bool commandFlagMarker, int dummy)
		{
			Symbol = null;
			TypeName = "Command";
			NamespaceName = null;
			FullName = "global::Command";
			Fields = new List<ComponentField>();
			IsReplicated = false;
			IsUnique = false;
			IsWatched = false;
			IsSynthesizedChangedFlag = false;
			IsSynthesizedOriginUserId = false;
			IsSynthesizedCommandFlag = true;
		}
	}
}
