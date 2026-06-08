using System.Text;

namespace RobloxCSharp.Extensions.Entities
{
	// Per-(context, component) interface that GameContext implements when
	// the component triggers Unique / EntityIndex / PrimaryEntityIndex
	// context-side bookkeeping. The cast site on the entity-side uses this
	// interface instead of the concrete {Ctx}Context — which breaks the
	// otherwise-inevitable transpiler-level circular import (GameEntity ↔
	// GameContext via the partial-emitted methods).
	//
	// Method signatures take IEntity (base) rather than {Ctx}Entity so the
	// interface file is a leaf in the dependency graph. The implementing
	// partial GameContext casts IEntity → {Ctx}Entity internally.
	internal static class ContextHooksInterfaceTemplate
	{
		public static void Emit(StringBuilder sb, ContextModel ctx, ComponentModel c)
		{
			sb.AppendLine($"public interface I{ctx.Name}{c.TypeName}ContextHooks");
			sb.AppendLine("{");

			if (c.IsUnique)
			{
				sb.AppendLine($"\tvoid _Set{c.TypeName}Entity(IEntity entity);");
				sb.AppendLine($"\tvoid _Clear{c.TypeName}Entity();");
			}

			int indexedCount = 0;
			for (int i = 0; i < c.Fields.Count; i++) if (c.Fields[i].IsIndexed) indexedCount++;

			foreach (ComponentField f in c.Fields)
			{
				if (!f.IsIndexed) continue;
				string suffix = indexedCount > 1 ? f.Name : "";
				sb.AppendLine($"\tvoid _Register{c.TypeName}{suffix}(IEntity entity, {f.TypeFullName} key);");
				sb.AppendLine($"\tvoid _Unregister{c.TypeName}{suffix}(IEntity entity, {f.TypeFullName} key);");
			}

			sb.AppendLine("}");
		}
	}
}
