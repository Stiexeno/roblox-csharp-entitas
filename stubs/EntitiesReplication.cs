#pragma warning disable CS0626 // Methods are implemented in runtime/EntitiesReplication.luau, not in this assembly.

namespace Entities
{
	// Replication wire for [Replicated] components. The codegen-emitted
	// AddX / ReplaceX / RemoveX tail every state mutation with a
	// Queue{Set,Remove} call, guarded by ShouldEmit so client / suppress
	// scopes skip the enqueue. The server runtime drains every context's
	// buffer on RunService.Heartbeat and fires one RemoteEvent per context
	// with the whole batch — bandwidth-friendly and ordering-stable across
	// components, unlike a per-component fanout.
	//
	// Wire shape (single RemoteEvent per context, lives at
	// ReplicatedStorage.Plugins.Entities.<Context>Replication):
	//   payload = array of ops, each op = {opcode, componentIndex, entityId, ...fields}
	//   opcode 0 = Set (covers Added + Replaced; client decides AddX vs ReplaceX by HasX)
	//   opcode 1 = Remove
	//
	// ShouldEmit returns:
	//   • true  on the server, normal path
	//   • false on the client (client-side AddX never enqueues)
	//   • false on the server inside a BeginSuppress/EndSuppress scope —
	//     reserved for future client→server flows; unused in the current
	//     server-authoritative alpha.
	public static class EntitiesReplication
	{
		public extern static bool ShouldEmit();
		public extern static void BeginSuppress();
		public extern static void EndSuppress();

		// Enqueue an Add-or-Replace op. Overload set covers 0..6 fields;
		// transpiler-side params would also work but explicit overloads
		// keep the lowering boring and the wire types obvious. If you have
		// a component with more than 6 public fields, extend this list.
		public extern static void QueueSet(string contextName, int componentIndex, int entityId);
		public extern static void QueueSet(string contextName, int componentIndex, int entityId, object v1);
		public extern static void QueueSet(string contextName, int componentIndex, int entityId, object v1, object v2);
		public extern static void QueueSet(string contextName, int componentIndex, int entityId, object v1, object v2, object v3);
		public extern static void QueueSet(string contextName, int componentIndex, int entityId, object v1, object v2, object v3, object v4);
		public extern static void QueueSet(string contextName, int componentIndex, int entityId, object v1, object v2, object v3, object v4, object v5);
		public extern static void QueueSet(string contextName, int componentIndex, int entityId, object v1, object v2, object v3, object v4, object v5, object v6);

		public extern static void QueueRemove(string contextName, int componentIndex, int entityId);

		// Subscribe a client-side handler. Called by the codegen-emitted
		// {Ctx}ClientReplication ctor exactly once per context. Handler
		// signature is `(ops)` where `ops` is the array decoded above.
		// First Subscribe on a client also fires the shared Ready event so
		// the server can dispatch a late-join snapshot back.
		public extern static void Subscribe(string contextName, object handler);

		// Server-only. Registers a per-context callable that returns the
		// snapshot ops array (every replicated component on every live
		// entity, encoded with opcode 0 = Set). The codegen-emitted
		// {Ctx}ServerReplication ctor calls this once per context. On
		// Players.PlayerAdded the runtime listens for the new client's
		// Ready ping; on receipt it walks every registered snapshotter and
		// FireClients the ops directly to that one player on the
		// per-context RemoteEvent.
		public extern static void RegisterSnapshotter(string contextName, object snapshot);

		// Per-context monotonic tick. Server-side counter increments on
		// every RunService.Heartbeat regardless of activity; every fired
		// batch (tick + snapshot) carries the value so the client can
		// track how far behind it is. Server: GetTick returns the current
		// local count. Client: GetServerTick returns the highest tick
		// received so far on that context — `serverTick - localExpected`
		// is a usable network-distance estimate, though there's no
		// quiet-period ping today (ticks only advance on the client when
		// the server actually fires something).
		public extern static int GetTick(string contextName);
		public extern static int GetServerTick(string contextName);
	}
}
