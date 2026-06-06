using System;

namespace Entitas.CodeGeneration.Attributes
{
	// Marks a component as server→client replicated. When the codegen
	// emits the entity's AddX / ReplaceX / RemoveX for a replicated
	// component, it appends a NetworkEvent fire (from the
	// roblox-csharp-networking plugin) carrying (entityId, fields...,
	// serverTick). The client subscribes via a generated per-context
	// `{Ctx}Replication` static class.
	//
	// Server-authoritative model: only the server side fires; the client
	// side calls SuppressEmit around its mirror-apply path so applying
	// a received event doesn't re-fire it. See README "Multiplayer" for
	// the prediction-friendly determinism rules.
	[AttributeUsage(AttributeTargets.Class)]
	public class ReplicatedAttribute : Attribute
	{
	}
}
