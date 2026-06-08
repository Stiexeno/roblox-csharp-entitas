#pragma warning disable CS0626 // Methods are implemented in runtime/EntitiesReplication.luau, not in this assembly.

namespace Entities
{
	// Runtime guard for the codegen-emitted replication fires. The server
	// runs `if (EntitiesReplication.ShouldEmit()) GameReplication.X?.Invoke(...)`
	// at the tail of every AddX / ReplaceX / RemoveX on a [Replicated]
	// component; the check returns:
	//
	//   • true  on the server, normal path
	//   • false on the client (calls to AddX are local-only there)
	//   • false on the server inside a BeginSuppress/EndSuppress scope —
	//     used by the client mirror when applying received events on
	//     server-shared code paths so the apply doesn't echo back.
	//
	// Counter-based so nested suppress scopes compose without stomping
	// each other.
	public static class EntitiesReplication
	{
		public extern static bool ShouldEmit();
		public extern static void BeginSuppress();
		public extern static void EndSuppress();
	}
}
