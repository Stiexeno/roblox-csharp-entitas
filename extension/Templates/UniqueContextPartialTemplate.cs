using System.Linq;
using System.Text;

namespace RobloxCSharp.Extensions.Entities
{
	// [Unique] support — emitted as a `partial class {Ctx}Context` block
	// inside the per-component file. Each Unique component contributes:
	//   • a private `_{name}Entity` field
	//   • public `{lowerName}Entity` / `is{Name}` (flag) or `{Name}` /
	//     `has{Name}` (value) accessors that read through the singleton
	//   • `Set{Name}(...)` / `Unset{Name}()` lifecycle methods
	//   • internal `_Set{Name}Entity(this)` / `_Clear{Name}Entity()` hooks
	//     called from the entity's AddX / ReplaceX / RemoveX / setter
	//     bodies so user code that mutates via the entity API stays in
	//     sync with the context's singleton field. Duplicate assignment
	//     throws — "Unique" means one entity at a time, matching Entitas.
	internal static class UniqueContextPartialTemplate
	{
		public static void Emit(StringBuilder sb, ContextModel ctx, ComponentModel c)
		{
			string entityType = $"{ctx.Name}Entity";
			string field = $"_{LowerFirst(c.TypeName)}Entity";
			string entityProp = $"{LowerFirst(c.TypeName)}Entity";

			sb.AppendLine($"public sealed partial class {ctx.Name}Context : I{ctx.Name}{c.TypeName}ContextHooks");
			sb.AppendLine("{");
			sb.AppendLine($"\tprivate {entityType} {field};");
			sb.AppendLine($"\tpublic {entityType} {entityProp} {{ get {{ return {field}; }} }}");

			if (c.IsFlag)
			{
				// Flag: `isPlayer` returns true iff the holder still carries it.
				sb.AppendLine($"\tpublic bool is{c.TypeName} {{ get {{ return {field} != null && {field}.Is{c.TypeName}; }} }}");

				// Set: create-on-demand, then flip the flag. Idempotent —
				// re-call when already set is a no-op.
				sb.AppendLine($"\tpublic {entityType} Set{c.TypeName}()");
				sb.AppendLine("\t{");
				sb.AppendLine($"\t\tif ({field} == null) {field} = CreateEntity();");
				sb.AppendLine($"\t\t{field}.Is{c.TypeName} = true;");
				sb.AppendLine($"\t\treturn {field};");
				sb.AppendLine("\t}");

				// Unset: destroy the holder (Entitas convention — Unique
				// entities are conventionally single-component, so destroy
				// is the cleanest cleanup; user can call entity APIs
				// directly if they want more granular control).
				sb.AppendLine($"\tpublic void Unset{c.TypeName}()");
				sb.AppendLine("\t{");
				sb.AppendLine($"\t\tif ({field} != null)");
				sb.AppendLine("\t\t{");
				sb.AppendLine($"\t\t\t{field}.Destroy();");
				sb.AppendLine($"\t\t\t{field} = null;");
				sb.AppendLine("\t\t}");
				sb.AppendLine("\t}");
			}
			else
			{
				bool unwrap = c.Fields.Count == 1 && c.Fields[0].Name == "Value";

				// `has{Name}` mirrors the entity-side Has check, gated by
				// the singleton presence.
				sb.AppendLine($"\tpublic bool has{c.TypeName} {{ get {{ return {field} != null && {field}.Has{c.TypeName}; }} }}");

				if (unwrap)
				{
					// Single-Value: expose the unwrapped value directly so
					// `gameContext.Health` returns `int`, not the component.
					string valueType = c.Fields[0].TypeFullName;
					sb.AppendLine($"\tpublic {valueType} {c.TypeName} {{ get {{ return {field} != null ? {field}.{c.TypeName} : default({valueType}); }} }}");
				}
				else
				{
					sb.AppendLine($"\tpublic {c.FullName} {LowerFirst(c.TypeName)} {{ get {{ return {field} != null ? {field}.{c.TypeName} : null; }} }}");
				}

				// Set / Replace: factory + assignment. Same ctor-arg shape
				// as entity AddX so caller doesn't have to think about
				// whether the holder exists yet.
				string ctorArgs = string.Join(", ", c.Fields.Select(f => $"{f.TypeFullName} new{f.Name}"));
				string callArgs = string.Join(", ", c.Fields.Select(f => $"new{f.Name}"));
				sb.AppendLine($"\tpublic {entityType} Set{c.TypeName}({ctorArgs})");
				sb.AppendLine("\t{");
				sb.AppendLine($"\t\tif ({field} == null)");
				sb.AppendLine("\t\t{");
				sb.AppendLine($"\t\t\t{field} = CreateEntity();");
				sb.AppendLine($"\t\t\t{field}.Add{c.TypeName}({callArgs});");
				sb.AppendLine("\t\t}");
				sb.AppendLine("\t\telse");
				sb.AppendLine("\t\t{");
				sb.AppendLine($"\t\t\t{field}.Replace{c.TypeName}({callArgs});");
				sb.AppendLine("\t\t}");
				sb.AppendLine($"\t\treturn {field};");
				sb.AppendLine("\t}");

				sb.AppendLine($"\tpublic {entityType} Replace{c.TypeName}({ctorArgs}) {{ return Set{c.TypeName}({callArgs}); }}");

				sb.AppendLine($"\tpublic void Unset{c.TypeName}()");
				sb.AppendLine("\t{");
				sb.AppendLine($"\t\tif ({field} != null)");
				sb.AppendLine("\t\t{");
				sb.AppendLine($"\t\t\t{field}.Destroy();");
				sb.AppendLine($"\t\t\t{field} = null;");
				sb.AppendLine("\t\t}");
				sb.AppendLine("\t}");
			}

			// Hooks — invoked by codegen-emitted entity bodies via the
			// I{Ctx}{Comp}ContextHooks interface (IEntity-typed parameter)
			// so the entity-side cast site doesn't have to import the
			// concrete {Ctx}Context type. Body casts IEntity → {Ctx}Entity
			// internally.
			sb.AppendLine($"\tpublic void _Set{c.TypeName}Entity(IEntity entity)");
			sb.AppendLine("\t{");
			sb.AppendLine($"\t\t{entityType} typed = ({entityType})entity;");
			sb.AppendLine($"\t\tif ({field} != null && {field} != typed)");
			sb.AppendLine("\t\t{");
			sb.AppendLine($"\t\t\tthrow new System.Exception(\"Unique component {c.TypeName} is already assigned to a different entity.\");");
			sb.AppendLine("\t\t}");
			sb.AppendLine($"\t\t{field} = typed;");
			sb.AppendLine("\t}");
			sb.AppendLine($"\tpublic void _Clear{c.TypeName}Entity() {{ {field} = null; }}");
			sb.AppendLine("}");
		}

		private static string LowerFirst(string s)
			=> string.IsNullOrEmpty(s) ? s : char.ToLowerInvariant(s[0]) + s.Substring(1);
	}
}
