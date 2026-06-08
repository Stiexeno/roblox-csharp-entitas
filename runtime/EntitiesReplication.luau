-- Runtime guard for codegen-emitted replication fires.
--
-- On the server, `ShouldEmit()` is true unless inside a BeginSuppress /
-- EndSuppress scope. On the client, `ShouldEmit()` is always false — even
-- though the codegen-emitted entity methods (AddX / ReplaceX / RemoveX)
-- run identically on both sides, the wire fire never goes through
-- client-side. The client mirror applies received events by calling the
-- same AddX / ReplaceX / RemoveX locally; ShouldEmit being false there
-- keeps the apply from echoing back.
--
-- BeginSuppress / EndSuppress are counter-based so nested scopes compose
-- (the mirror could end up in a stack of applies via a chained reaction).

local RunService = game:GetService("RunService")

local EntitiesReplication = {}
EntitiesReplication._suppressCount = 0
EntitiesReplication._isServer = RunService:IsServer()

function EntitiesReplication.ShouldEmit()
	return EntitiesReplication._isServer and EntitiesReplication._suppressCount == 0
end

function EntitiesReplication.BeginSuppress()
	EntitiesReplication._suppressCount = EntitiesReplication._suppressCount + 1
end

function EntitiesReplication.EndSuppress()
	EntitiesReplication._suppressCount = EntitiesReplication._suppressCount - 1
	if EntitiesReplication._suppressCount < 0 then
		error("EntitiesReplication.EndSuppress called more times than BeginSuppress.")
	end
end

return EntitiesReplication
