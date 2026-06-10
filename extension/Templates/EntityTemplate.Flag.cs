using System.Text;

namespace RobloxCSharp.Extensions.Entities
{
	internal static partial class EntityTemplate
	{
		// Flag → `entity.Is{Name}` (PascalCase, gettable+settable bool).
		// Setter swaps the shared singleton instance in/out so we don't
		// allocate per flip. Replicated flags enqueue Set/Remove ops on
		// the per-context replication buffer, guarded by ShouldEmit so
		// the client / suppress scopes skip the enqueue. [Unique] flags
		// tail-update the context's singleton field through the codegen-
		// emitted _Set/_Clear hook — the entity API stays the same shape,
		// the context just tracks who holds the flag.
		//
		// Context-capture pattern (`{ var _ctx = (Ctx)context; if … }`)
		// halves the `self:context()` accessor count in Lua: the cast
		// evaluates `context` once and the null check then reads the
		// local — instead of two accessor calls per hook fire.
		//
		// [Watched] flag — both branches of the Is{X} setter raise the
		// Changed signal because a flag flip is a change in either
		// direction.
		internal static void EmitFlag(StringBuilder sb, ContextModel ctx, ComponentModel c, string lookup)
		{
			string fireAdd = c.IsReplicated
				? $" if (EntitiesReplication.ShouldEmit()) EntitiesReplication.QueueSet(\"{ctx.Name}\", {lookup}, creationIndex);"
				: "";
			string fireRemove = c.IsReplicated
				? $" if (EntitiesReplication.ShouldEmit()) EntitiesReplication.QueueRemove(\"{ctx.Name}\", {lookup}, creationIndex);"
				: "";

			// Synthesized Command flag — this is the client→server ship
			// trigger. True branch marks the entity in the pending set
			// (heartbeat drain will serialize + fire). False branch
			// unmarks so toggling off pre-drain cancels the ship cleanly.
			// Guarded by ShouldSendCommand so the server-side `IsCommand =
			// true` (e.g., in CommandReceiver) is a pure component flip
			// with no wire effect.
			string markCommandPending = c.IsSynthesizedCommandFlag
				? $" if (EntitiesReplication.ShouldSendCommand()) EntitiesReplication.MarkCommandPending(\"{ctx.Name}\", this);"
				: "";
			string unmarkCommandPending = c.IsSynthesizedCommandFlag
				? $" if (EntitiesReplication.ShouldSendCommand()) EntitiesReplication.UnmarkCommandPending(\"{ctx.Name}\", this);"
				: "";
			string uniqueSet = c.IsUnique
				? $" {{ I{ctx.Name}{c.TypeName}ContextHooks _ctx = context as I{ctx.Name}{c.TypeName}ContextHooks; if (_ctx != null) _ctx._Set{c.TypeName}Entity(this); }}"
				: "";
			string uniqueClear = c.IsUnique
				? $" {{ I{ctx.Name}{c.TypeName}ContextHooks _ctx = context as I{ctx.Name}{c.TypeName}ContextHooks; if (_ctx != null) _ctx._Clear{c.TypeName}Entity(); }}"
				: "";
			string watchedFlag = c.IsWatched
				? $" Is{c.TypeName}Changed = true;"
				: "";

			sb.AppendLine($"\tstatic readonly {c.FullName} _{c.TypeName}Component = new();");
			sb.AppendLine($"\tpublic bool Is{c.TypeName}");
			sb.AppendLine("\t{");
			sb.AppendLine($"\t\tget {{ return HasComponent({lookup}); }}");
			sb.AppendLine("\t\tset");
			sb.AppendLine("\t\t{");
			sb.AppendLine($"\t\t\tif (value != Is{c.TypeName})");
			sb.AppendLine("\t\t\t{");
			sb.AppendLine($"\t\t\t\tif (value) {{ AddComponent({lookup}, _{c.TypeName}Component);{fireAdd}{markCommandPending}{uniqueSet}{watchedFlag} }}");
			sb.AppendLine($"\t\t\t\telse {{ RemoveComponent({lookup});{fireRemove}{unmarkCommandPending}{uniqueClear}{watchedFlag} }}");
			sb.AppendLine("\t\t\t}");
			sb.AppendLine("\t\t}");
			sb.AppendLine("\t}");
		}
	}
}
