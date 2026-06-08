#pragma warning disable CS0626 // Methods are implemented in runtime/EntitiesReplication.luau, not in this assembly.

namespace Entities
{
	// Replication wire for [Replicated] components. Binary-packed via
	// Roblox `buffer` — ~3-5x smaller than the per-op Lua-table wire,
	// near-zero per-tick GC on the server-side pack path.
	//
	// Per-context flow:
	//   • Server-side {Ctx}ServerReplication ctor calls
	//     RegisterComponentSchema once per [Replicated] component to tell
	//     the runtime the field types it should pack.
	//   • Codegen-emitted entity AddX / ReplaceX / RemoveX bodies call
	//     QueueSet / QueueRemove — same C# signature as before, but the
	//     runtime now writes into a per-context buffer via the schema
	//     instead of building a Lua table.
	//   • Heartbeat drains every dirty buffer and FireAllClients's
	//     (tick, count, buffer) on the per-context RemoteEvent.
	//
	//   • Client-side {Ctx}ClientReplication ctor calls
	//     RegisterComponentSchema (same schema, mirrored), then registers
	//     a typed applier + remover per component via
	//     RegisterClientApplier / RegisterClientRemover, then calls
	//     SubscribeClient to wire OnClientEvent + the Ready ping.
	//   • On batch receipt the runtime unpacks per the schema and calls
	//     applier(entityId, ...fields) / remover(entityId).
	//
	// Wire format per op:
	//   1 byte  opcode (0 = Set, 1 = Remove)
	//   1 byte  componentIndex (u8 — supports up to 256 components per
	//           context, plenty in practice)
	//   4 bytes entityId (i32)
	//   N bytes packed fields (Set only; schema determines layout)
	//
	// Late join: shared Ready RemoteEvent (also under Plugins.Entities)
	// — client fires once on first SubscribeClient, server walks each
	// registered snapshotter and FireClient's the resulting buffer to
	// that one player on the per-context channel.
	public static class EntitiesReplication
	{
		public extern static bool ShouldEmit();
		public extern static void BeginSuppress();
		public extern static void EndSuppress();

		// Enqueue an Add-or-Replace op. Overload set covers 0..6 fields.
		// Each overload's `object` args lower to Luau natives via
		// vararg dispatch (no boxing at runtime); the runtime pulls them
		// out via select(i, ...) so there's no varargs table allocation
		// per call.
		public extern static void QueueSet(string contextName, int componentIndex, int entityId);
		public extern static void QueueSet(string contextName, int componentIndex, int entityId, object v1);
		public extern static void QueueSet(string contextName, int componentIndex, int entityId, object v1, object v2);
		public extern static void QueueSet(string contextName, int componentIndex, int entityId, object v1, object v2, object v3);
		public extern static void QueueSet(string contextName, int componentIndex, int entityId, object v1, object v2, object v3, object v4);
		public extern static void QueueSet(string contextName, int componentIndex, int entityId, object v1, object v2, object v3, object v4, object v5);
		public extern static void QueueSet(string contextName, int componentIndex, int entityId, object v1, object v2, object v3, object v4, object v5, object v6);

		public extern static void QueueRemove(string contextName, int componentIndex, int entityId);

		// Server-only. Registers a per-context callable that returns the
		// snapshot ops via QueueSet calls. Codegen-emitted
		// {Ctx}ServerReplication ctor calls this once per context. On
		// PlayerAdded → Ready, the runtime sets snapshot mode, invokes the
		// snapshotter (so its QueueSet calls land in the per-player snap
		// buffer instead of the shared tick buffer), then fires the
		// resulting buffer to that one player.
		public extern static void RegisterSnapshotter(string contextName, object snapshot);

		// Schema registration — called by both server and client during
		// their per-context bootstrap. fieldTypes is an array of
		// EntitiesFieldType.X codes in declaration order. Empty/missing
		// schema means a flag component (no fields). Server uses it to
		// pack; client uses it to unpack before calling the applier.
		public extern static void RegisterComponentSchema(string contextName, int componentIndex, int[] fieldTypes);

		// Client-only. Per-component callables invoked by the runtime
		// after it unpacks a Set / Remove op from a received batch. Set
		// signature is `(int entityId, ...fields)` matching the schema;
		// Remove signature is `(int entityId)`. The applier should pick
		// Add-vs-Replace by HasX so repeats from snapshots or re-sends
		// stay idempotent.
		public extern static void RegisterClientApplier(string contextName, int componentIndex, object applier);
		public extern static void RegisterClientRemover(string contextName, int componentIndex, object remover);

		// Client-only. Wires the per-context RemoteEvent's OnClientEvent
		// to the runtime's unpack-and-dispatch loop, and fires the Ready
		// ping once per client session so the server snapshots back.
		// Called by codegen-emitted {Ctx}ClientReplication ctor after
		// every Register call so the runtime has everything wired before
		// the first batch arrives.
		public extern static void SubscribeClient(string contextName);

		// Per-context monotonic tick. Server-side counter advances on
		// every Heartbeat; every fired batch (tick + snapshot) carries
		// the value. GetServerTick on the client returns the highest
		// received — `serverTick - localExpected` is a usable
		// network-distance estimate.
		public extern static int GetTick(string contextName);
		public extern static int GetServerTick(string contextName);
	}

	// Field type codes used by RegisterComponentSchema. Values are wire-
	// stable bytes; don't renumber after release without bumping a wire
	// version. Mapped from C# field types in the codegen.
	public static class EntitiesFieldType
	{
		public const int I32 = 0;     // C# int (4 bytes)
		public const int F32 = 1;     // C# float (4 bytes)
		public const int F64 = 2;     // C# double (8 bytes)
		public const int Bool = 3;    // C# bool (1 byte, 0/1)
		public const int String = 4;  // u16 length + bytes
		public const int U8 = 5;      // C# byte (1 byte)
		public const int U16 = 6;     // C# ushort (2 bytes)
		public const int U32 = 7;     // C# uint (4 bytes)
		public const int I8 = 8;      // C# sbyte (1 byte)
		public const int I16 = 9;     // C# short (2 bytes)
	}
}
