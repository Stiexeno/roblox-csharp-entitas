-- Replication wire. Single RemoteEvent per context (lives at
-- ReplicatedStorage.Plugins.Entities.<Context>Replication), payload is an
-- array of ops, each op = {opcode, componentIndex, entityId, ...fields}.
-- Opcode 0 = Set (covers Added + Replaced; client decides AddX vs
-- ReplaceX by HasX). Opcode 1 = Remove.
--
-- Server: codegen-emitted entity AddX/ReplaceX/RemoveX bodies enqueue via
-- QueueSet / QueueRemove. RunService.Heartbeat tail drains every context's
-- buffer at end of frame and fires the per-context RemoteEvent once with
-- the whole batch. One RemoteEvent fire per tick per dirty context — far
-- cheaper than fanning out per-component delegate calls.
--
-- Client: ShouldEmit is always false there, so codegen-emitted bodies
-- skip the enqueue. The codegen-emitted {Ctx}ClientReplication subscribes
-- once via Subscribe(name, handler) and dispatches received ops by
-- componentIndex onto the local entity. BeginSuppress / EndSuppress are
-- counter-based so nested suppress scopes compose — reserved for a future
-- client→server flow; the current alpha is server-authoritative only.

local RunService = game:GetService("RunService")
local ReplicatedStorage = game:GetService("ReplicatedStorage")

local EntitiesReplication = {}
EntitiesReplication._suppressCount = 0
EntitiesReplication._isServer = RunService:IsServer()
EntitiesReplication._buffers = {}      -- contextName -> array of ops
EntitiesReplication._events = {}       -- contextName -> RemoteEvent
EntitiesReplication._flushConnected = false

-- Plugins/Entities folder where every per-context RemoteEvent lives.
-- Server lazy-creates it on first QueueSet; client lazy-waits on first
-- Subscribe. Roblox guarantees ReplicatedStorage exists before scripts
-- run, so no readiness gate beyond WaitForChild.
local function pluginsFolder()
	local plugins = ReplicatedStorage:WaitForChild("Plugins")
	return plugins:WaitForChild("Entities")
end

local function getOrCreateEvent(contextName)
	local existing = EntitiesReplication._events[contextName]
	if existing ~= nil then return existing end

	local parent = pluginsFolder()
	local eventName = contextName .. "Replication"

	if EntitiesReplication._isServer then
		local inst = parent:FindFirstChild(eventName)
		if inst == nil then
			inst = Instance.new("RemoteEvent")
			inst.Name = eventName
			inst.Parent = parent
		end
		EntitiesReplication._events[contextName] = inst
		return inst
	else
		local inst = parent:WaitForChild(eventName)
		EntitiesReplication._events[contextName] = inst
		return inst
	end
end

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

-- Server-side flush. Drains every context's buffer; fires one
-- RemoteEvent per non-empty context with the whole batch.
local function flushAll()
	for contextName, buf in pairs(EntitiesReplication._buffers) do
		if #buf > 0 then
			local ev = getOrCreateEvent(contextName)
			ev:FireAllClients(buf)
			EntitiesReplication._buffers[contextName] = {}
		end
	end
end

local function ensureFlushHook()
	if EntitiesReplication._flushConnected then return end
	if not EntitiesReplication._isServer then return end
	RunService.Heartbeat:Connect(flushAll)
	EntitiesReplication._flushConnected = true
end

local function bufferFor(contextName)
	local buf = EntitiesReplication._buffers[contextName]
	if buf == nil then
		buf = {}
		EntitiesReplication._buffers[contextName] = buf
		-- Pre-warm the RemoteEvent + flush hook lazily on first use.
		getOrCreateEvent(contextName)
		ensureFlushHook()
	end
	return buf
end

-- Set op (Add + Replace collapse to one wire shape; client distinguishes
-- by HasX). Variadic over the field values — the C# stub exposes 0..6
-- explicit-arity overloads, all of which lower to this single function.
function EntitiesReplication.QueueSet(contextName, componentIndex, entityId, ...)
	if not EntitiesReplication._isServer then return end
	local op = {0, componentIndex, entityId, ...}
	table.insert(bufferFor(contextName), op)
end

function EntitiesReplication.QueueRemove(contextName, componentIndex, entityId)
	if not EntitiesReplication._isServer then return end
	table.insert(bufferFor(contextName), {1, componentIndex, entityId})
end

-- Client-side single subscription point. The codegen-emitted
-- {Ctx}ClientReplication ctor calls this exactly once per context with a
-- handler that loops the ops array and dispatches by componentIndex.
function EntitiesReplication.Subscribe(contextName, handler)
	if EntitiesReplication._isServer then return end
	local ev = getOrCreateEvent(contextName)
	ev.OnClientEvent:Connect(handler)
end

return EntitiesReplication
