using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace RobloxCSharp.Extensions.Entities
{
	internal static partial class EntityTemplate
	{
		// Value component → AddX / ReplaceX / RemoveX / property getter
		// (single-Value field unwraps to the value type, multi-field
		// returns the component instance) + HasX getter. Hooks fire after
		// the mutation:
		//   • [Replicated]:  QueueSet / QueueRemove on the wire buffer
		//   • [Unique]:      _Set{X}Entity / _Clear{X}Entity on the context
		//   • [EntityIndex]: _Register / _Unregister per indexed field
		//   • [Watched]:     Is{X}Changed = true on Add / Replace / setter
		//                    (Remove deliberately skips — entity no longer
		//                    has the component, the Changed matcher can't
		//                    observe it anyway)
		//
		// [Unique] + [EntityIndex] hooks share one
		// `{ var _ctx = (Ctx)context; if (_ctx != null) {…} }` block per
		// emit site so we only do one `self:context()` call in the Lua
		// emit — halves the accessor calls vs one-per-hook.
		internal static void EmitValue(StringBuilder sb, ContextModel ctx, ComponentModel c, string lookup)
		{
			string ctorArgs = string.Join(", ", c.Fields.Select(f => $"{f.TypeFullName} new{f.Name}"));
			string fieldAssigns = string.Join(" ", c.Fields.Select(f => $"component.{f.Name} = new{f.Name};"));

			// Single field named `Value` → unwrap to the field's type:
			//   `entity.Health` returns `int`, not the Health component.
			// Anything else (multi-field, or single non-`Value` field) →
			// return the component instance so `entity.Position.X` works.
			bool unwrap = c.Fields.Count == 1 && c.Fields[0].Name == "Value";

			string setArgs = $"\"{ctx.Name}\", {lookup}, creationIndex";
			foreach (ComponentField f in c.Fields) setArgs += ", new" + f.Name;
			string fireSet = c.IsReplicated
				? $"\t\tif (EntitiesReplication.ShouldEmit()) EntitiesReplication.QueueSet({setArgs});"
				: "";
			string fireRemove = c.IsReplicated
				? $"\t\tif (EntitiesReplication.ShouldEmit()) EntitiesReplication.QueueRemove(\"{ctx.Name}\", {lookup}, creationIndex);"
				: "";

			int indexedFieldCount = 0;
			for (int fi = 0; fi < c.Fields.Count; fi++) if (c.Fields[fi].IsIndexed) indexedFieldCount++;
			List<ComponentField> indexedFields = c.Fields.Where(f => f.IsIndexed).ToList();
			string IndexSuffix(ComponentField f) => indexedFieldCount > 1 ? f.Name : "";
			string ReadField(ComponentField f) => (unwrap && f.Name == "Value") ? c.TypeName : $"{c.TypeName}.{f.Name}";

			bool HasContextHooks() => c.IsUnique || indexedFields.Count > 0;
			void EmitContextBlock(string indent, Action<string> body)
			{
				if (!HasContextHooks()) return;
				sb.AppendLine($"{indent}{{");
				sb.AppendLine($"{indent}\t{ctx.Name}Context _ctx = ({ctx.Name}Context)context;");
				sb.AppendLine($"{indent}\tif (_ctx != null)");
				sb.AppendLine($"{indent}\t{{");
				body($"{indent}\t\t");
				sb.AppendLine($"{indent}\t}}");
				sb.AppendLine($"{indent}}}");
			}

			if (unwrap)
			{
				string valueType = c.Fields[0].TypeFullName;
				string fireSetter = c.IsReplicated
					? $"\t\t\tif (EntitiesReplication.ShouldEmit()) EntitiesReplication.QueueSet(\"{ctx.Name}\", {lookup}, creationIndex, value);"
					: "";
				sb.AppendLine($"\tpublic {valueType} {c.TypeName}");
				sb.AppendLine("\t{");
				sb.AppendLine($"\t\tget {{ return (({c.FullName})GetComponent({lookup})).Value; }}");
				sb.AppendLine("\t\tset");
				sb.AppendLine("\t\t{");
				// Index prev capture — Value-indexed only (unwrap implies one field).
				if (c.Fields[0].IsIndexed)
				{
					sb.AppendLine($"\t\t\t{valueType} _prevValue = Has{c.TypeName} ? {c.TypeName} : default({valueType});");
					sb.AppendLine($"\t\t\tbool _had{c.TypeName} = Has{c.TypeName};");
				}
				sb.AppendLine($"\t\t\t{c.FullName} component = CreateComponent<{c.FullName}>({lookup});");
				sb.AppendLine($"\t\t\tcomponent.Value = value;");
				sb.AppendLine($"\t\t\tReplaceComponent({lookup}, component);");
				if (c.IsReplicated) sb.AppendLine(fireSetter);
				EmitContextBlock("\t\t\t", indent =>
				{
					if (c.IsUnique) sb.AppendLine($"{indent}_ctx._Set{c.TypeName}Entity(this);");
					if (c.Fields[0].IsIndexed)
					{
						ComponentField f = c.Fields[0];
						string suffix = IndexSuffix(f);
						sb.AppendLine($"{indent}if (_had{c.TypeName}) _ctx._Unregister{c.TypeName}{suffix}(this, _prevValue);");
						sb.AppendLine($"{indent}_ctx._Register{c.TypeName}{suffix}(this, value);");
					}
				});
				if (c.IsWatched) sb.AppendLine($"\t\t\tIs{c.TypeName}Changed = true;");
				sb.AppendLine("\t\t}");
				sb.AppendLine("\t}");
			}
			else
			{
				sb.AppendLine($"\tpublic {c.FullName} {c.TypeName} {{ get {{ return ({c.FullName})GetComponent({lookup}); }} }}");
			}
			sb.AppendLine($"\tpublic bool Has{c.TypeName} {{ get {{ return HasComponent({lookup}); }} }}");

			// AddX/ReplaceX pull from the component pool when one's
			// available. The recycled instance has whatever state was on it
			// before; the field assignments below overwrite anything we care
			// about.
			sb.AppendLine();
			sb.AppendLine($"\tpublic {c.FullName} Add{c.TypeName}({ctorArgs})");
			sb.AppendLine("\t{");
			sb.AppendLine($"\t\t{c.FullName} component = CreateComponent<{c.FullName}>({lookup});");
			sb.AppendLine($"\t\t{fieldAssigns}");
			sb.AppendLine($"\t\tAddComponent({lookup}, component);");
			if (c.IsReplicated) sb.AppendLine(fireSet);
			EmitContextBlock("\t\t", indent =>
			{
				if (c.IsUnique) sb.AppendLine($"{indent}_ctx._Set{c.TypeName}Entity(this);");
				foreach (ComponentField f in indexedFields)
				{
					string suffix = IndexSuffix(f);
					sb.AppendLine($"{indent}_ctx._Register{c.TypeName}{suffix}(this, new{f.Name});");
				}
			});
			if (c.IsWatched) sb.AppendLine($"\t\tIs{c.TypeName}Changed = true;");
			sb.AppendLine("\t\treturn component;");
			sb.AppendLine("\t}");

			sb.AppendLine();
			sb.AppendLine($"\tpublic {c.FullName} Replace{c.TypeName}({ctorArgs})");
			sb.AppendLine("\t{");
			foreach (ComponentField f in indexedFields)
			{
				sb.AppendLine($"\t\t{f.TypeFullName} _prev{f.Name} = Has{c.TypeName} ? {ReadField(f)} : default({f.TypeFullName});");
			}
			if (indexedFields.Count > 0)
				sb.AppendLine($"\t\tbool _had{c.TypeName} = Has{c.TypeName};");
			sb.AppendLine($"\t\t{c.FullName} component = CreateComponent<{c.FullName}>({lookup});");
			sb.AppendLine($"\t\t{fieldAssigns}");
			sb.AppendLine($"\t\tReplaceComponent({lookup}, component);");
			if (c.IsReplicated) sb.AppendLine(fireSet);
			EmitContextBlock("\t\t", indent =>
			{
				if (c.IsUnique) sb.AppendLine($"{indent}_ctx._Set{c.TypeName}Entity(this);");
				foreach (ComponentField f in indexedFields)
				{
					string suffix = IndexSuffix(f);
					sb.AppendLine($"{indent}if (_had{c.TypeName}) _ctx._Unregister{c.TypeName}{suffix}(this, _prev{f.Name});");
					sb.AppendLine($"{indent}_ctx._Register{c.TypeName}{suffix}(this, new{f.Name});");
				}
			});
			if (c.IsWatched) sb.AppendLine($"\t\tIs{c.TypeName}Changed = true;");
			sb.AppendLine("\t\treturn component;");
			sb.AppendLine("\t}");

			sb.AppendLine();
			sb.AppendLine($"\tpublic void Remove{c.TypeName}()");
			sb.AppendLine("\t{");
			foreach (ComponentField f in indexedFields)
			{
				sb.AppendLine($"\t\t{f.TypeFullName} _prev{f.Name} = Has{c.TypeName} ? {ReadField(f)} : default({f.TypeFullName});");
			}
			if (indexedFields.Count > 0)
				sb.AppendLine($"\t\tbool _had{c.TypeName} = Has{c.TypeName};");
			sb.AppendLine($"\t\tRemoveComponent({lookup});");
			if (c.IsReplicated) sb.AppendLine(fireRemove);
			EmitContextBlock("\t\t", indent =>
			{
				if (c.IsUnique) sb.AppendLine($"{indent}_ctx._Clear{c.TypeName}Entity();");
				foreach (ComponentField f in indexedFields)
				{
					string suffix = IndexSuffix(f);
					sb.AppendLine($"{indent}if (_had{c.TypeName}) _ctx._Unregister{c.TypeName}{suffix}(this, _prev{f.Name});");
				}
			});
			sb.AppendLine("\t}");
		}
	}
}
