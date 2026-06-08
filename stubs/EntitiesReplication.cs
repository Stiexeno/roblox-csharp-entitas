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
		public extern static void Subscribe(string contextName, object handler);
	}
}
