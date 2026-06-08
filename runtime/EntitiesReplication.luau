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
--
-- Late join: a shared Ready RemoteEvent (also under Plugins.Entities)
-- carries a client→server "I'm subscribed, send me the world" ping. The
-- client fires it exactly once on its first Subscribe call. The server
-- listens once on RegisterSnapshotter; on receipt it walks every
-- registered per-context snapshotter and FireClients the resulting ops
-- batch directly to that one player on the per-context RemoteEvent.
-- ApplySet on the client picks Add-vs-Replace by HasX so any tick-time
-- op that races the snapshot stays idempotent.

local RunService = game:GetService("RunService")
local ReplicatedStorage = game:GetService("ReplicatedStorage")

local EntitiesReplication = {}
EntitiesReplication._suppressCount = 0
EntitiesReplication._isServer = RunService:IsServer()
EntitiesReplication._buffers = {}            -- contextName -> array of ops
EntitiesReplication._events = {}             -- contextName -> RemoteEvent
EntitiesReplication._snapshotters = {}       -- contextName -> () -> ops array (server-only)
EntitiesReplication._flushConnected = false
EntitiesReplication._readyConnected = false  -- server: OnServerEvent wired
EntitiesReplication._readyFired = false      -- client: fired once per session

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

-- Single shared Ready RemoteEvent. Client fires once on first Subscribe;
-- server listens once when the first snapshotter registers.
local function getOrCreateReadyEvent()
	local parent = pluginsFolder()
	if EntitiesReplication._isServer then
		local inst = parent:FindFirstChild("Ready")
		if inst == nil then
			inst = Instance.new("RemoteEvent")
			inst.Name = "Ready"
			inst.Parent = parent
		end
		return inst
	else
		return parent:WaitForChild("Ready")
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

local function ensureReadyListener()
	if EntitiesReplication._readyConnected then return end
	if not EntitiesReplication._isServer then return end
	EntitiesReplication._readyConnected = true
	local readyEv = getOrCreateReadyEvent()
	-- pcall the snapshotter so a buggy user fn for one context doesn't
	-- starve the others for this player.
	readyEv.OnServerEvent:Connect(function(player)
		for contextName, snapshotFn in pairs(EntitiesReplication._snapshotters) do
			local ok, ops = pcall(snapshotFn)
			if not ok then
				warn("EntitiesReplication: snapshotter for context '" .. contextName .. "' errored: " .. tostring(ops))
			elseif ops ~= nil and #ops > 0 then
				local ctxEv = getOrCreateEvent(contextName)
				ctxEv:FireClient(player, ops)
			end
		end
	end)
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
-- First call also pings the server's Ready event so it can dispatch a
-- late-join snapshot back; subsequent Subscribes don't re-ping.
function EntitiesReplication.Subscribe(contextName, handler)
	if EntitiesReplication._isServer then return end
	local ev = getOrCreateEvent(contextName)
	ev.OnClientEvent:Connect(handler)
	if not EntitiesReplication._readyFired then
		EntitiesReplication._readyFired = true
		local readyEv = getOrCreateReadyEvent()
		readyEv:FireServer()
	end
end

-- Server-only. The codegen-emitted {Ctx}ServerReplication ctor registers
-- a snapshotter once per context. The fn is called per joining player
-- and should return the ops array for the whole world right now.
function EntitiesReplication.RegisterSnapshotter(contextName, snapshot)
	if not EntitiesReplication._isServer then return end
	EntitiesReplication._snapshotters[contextName] = snapshot
	-- Pre-warm the context's RemoteEvent + Ready listener so the first
	-- joining client has somewhere to fire and somewhere to be heard.
	getOrCreateEvent(contextName)
	ensureReadyListener()
end

return EntitiesReplication
