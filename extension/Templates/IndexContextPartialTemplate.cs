using System.Text;

namespace RobloxCSharp.Extensions.Entities
{
	// [EntityIndex] / [PrimaryEntityIndex] support — emitted as a
	// `partial class {Ctx}Context` block per indexed field. Each indexed
	// field contributes:
	//   • a private dict: `Dictionary<TKey, {Ctx}Entity>` for primary,
	//     `Dictionary<TKey, HashSet<{Ctx}Entity>>` for non-primary
	//   • a public lookup: `GetEntityWith{Component}(TKey)` returning the
	//     single entity (or null) for primary, or
	//     `GetEntitiesWith{Component}(TKey)` returning an IEnumerable for
	//     non-primary
	//   • internal `_Register{Component}` / `_Unregister{Component}` hooks
	//     called from the entity's AddX / ReplaceX / RemoveX / setter
	//     bodies via the I{Ctx}{Comp}ContextHooks interface — IEntity-typed
	//     parameter so the entity-side cast site doesn't import the
	//     concrete {Ctx}Context (avoiding a transpiler-level circular
	//     import). Bodies cast IEntity → {Ctx}Entity internally.
	// If a component has more than one indexed field, the method/hook
	// names are disambiguated with the field name suffix.
	internal static class IndexContextPartialTemplate
	{
		public static void Emit(StringBuilder sb, ContextModel ctx, ComponentModel c)
		{
			string entityType = $"{ctx.Name}Entity";
			string hooks = $"I{ctx.Name}{c.TypeName}ContextHooks";
			int indexedCount = 0;
			for (int i = 0; i < c.Fields.Count; i++) if (c.Fields[i].IsIndexed) indexedCount++;

			sb.AppendLine($"public sealed partial class {ctx.Name}Context : {hooks}");
			sb.AppendLine("{");
			foreach (ComponentField f in c.Fields)
			{
				if (!f.IsIndexed) continue;
				string suffix = indexedCount > 1 ? f.Name : "";
				string keyType = f.TypeFullName;
				string dictField = $"_{LowerFirst(c.TypeName)}By{f.Name}";

				if (f.IsPrimaryIndexed)
				{
					sb.AppendLine($"\tprivate System.Collections.Generic.Dictionary<{keyType}, {entityType}> {dictField} = new();");

					sb.AppendLine($"\tpublic {entityType} GetEntityWith{c.TypeName}{suffix}({keyType} key)");
					sb.AppendLine("\t{");
					sb.AppendLine($"\t\tif ({dictField}.ContainsKey(key)) return {dictField}[key];");
					sb.AppendLine("\t\treturn null;");
					sb.AppendLine("\t}");

					sb.AppendLine($"\tpublic void _Register{c.TypeName}{suffix}(IEntity entity, {keyType} key)");
					sb.AppendLine("\t{");
					sb.AppendLine($"\t\t{entityType} typed = ({entityType})entity;");
					sb.AppendLine($"\t\tif ({dictField}.ContainsKey(key) && {dictField}[key] != typed)");
					sb.AppendLine($"\t\t\tthrow new System.Exception(\"PrimaryEntityIndex collision on {c.TypeName}.{f.Name} for key: \" + key);");
					sb.AppendLine($"\t\t{dictField}[key] = typed;");
					sb.AppendLine("\t}");

					sb.AppendLine($"\tpublic void _Unregister{c.TypeName}{suffix}(IEntity entity, {keyType} key)");
					sb.AppendLine("\t{");
					sb.AppendLine($"\t\t{entityType} typed = ({entityType})entity;");
					sb.AppendLine($"\t\tif ({dictField}.ContainsKey(key) && {dictField}[key] == typed) {dictField}.Remove(key);");
					sb.AppendLine("\t}");
				}
				else
				{
					sb.AppendLine($"\tprivate System.Collections.Generic.Dictionary<{keyType}, System.Collections.Generic.HashSet<{entityType}>> {dictField} = new();");

					sb.AppendLine($"\tpublic System.Collections.Generic.IEnumerable<{entityType}> GetEntitiesWith{c.TypeName}{suffix}({keyType} key)");
					sb.AppendLine("\t{");
					sb.AppendLine($"\t\tif ({dictField}.ContainsKey(key)) return {dictField}[key];");
					sb.AppendLine($"\t\treturn System.Linq.Enumerable.Empty<{entityType}>();");
					sb.AppendLine("\t}");

					sb.AppendLine($"\tpublic void _Register{c.TypeName}{suffix}(IEntity entity, {keyType} key)");
					sb.AppendLine("\t{");
					sb.AppendLine($"\t\t{entityType} typed = ({entityType})entity;");
					sb.AppendLine($"\t\tSystem.Collections.Generic.HashSet<{entityType}> set;");
					sb.AppendLine($"\t\tif ({dictField}.ContainsKey(key)) {{ set = {dictField}[key]; }}");
					sb.AppendLine($"\t\telse {{ set = new System.Collections.Generic.HashSet<{entityType}>(); {dictField}[key] = set; }}");
					sb.AppendLine("\t\tset.Add(typed);");
					sb.AppendLine("\t}");

					sb.AppendLine($"\tpublic void _Unregister{c.TypeName}{suffix}(IEntity entity, {keyType} key)");
					sb.AppendLine("\t{");
					sb.AppendLine($"\t\t{entityType} typed = ({entityType})entity;");
					sb.AppendLine($"\t\tif ({dictField}.ContainsKey(key)) {dictField}[key].Remove(typed);");
					sb.AppendLine("\t}");
				}
			}
			sb.AppendLine("}");
		}

		private static string LowerFirst(string s)
			=> string.IsNullOrEmpty(s) ? s : char.ToLowerInvariant(s[0]) + s.Substring(1);
	}
}
