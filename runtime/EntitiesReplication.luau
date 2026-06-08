-- Replication wire — binary-packed via Roblox `buffer`.
--
-- One RemoteEvent per context at ReplicatedStorage.Plugins.Entities.
-- <Context>Replication. Every fire carries (tick, count, buffer) where
-- the buffer holds `count` packed ops sequentially:
--   1 byte  opcode (0 = Set, 1 = Remove)
--   1 byte  componentIndex
--   4 bytes entityId (i32)
--   N bytes packed fields (Set only; field types per the registered
--           per-component schema)
--
-- Schemas:
--   • Server-side {Ctx}ServerReplication ctor registers schemas once per
--     [Replicated] component (RegisterComponentSchema).
--   • Client-side {Ctx}ClientReplication ctor registers the matching
--     schemas + typed appliers/removers + then calls SubscribeClient to
--     wire OnClientEvent into the unpack/dispatch loop.
--
-- Server flow:
--   • Codegen-emitted entity AddX/ReplaceX/RemoveX bodies call
--     QueueSet/QueueRemove. The runtime packs into a per-context tick
--     buffer (or, during a snapshot call, into a separate snap buffer).
--   • Heartbeat tail drains every non-empty tick buffer and fires it
--     once per context with the whole batch.
--   • The Heartbeat counter also advances even on quiet contexts so the
--     `serverTick` value reflects wall-clock progress.
--
-- Client flow:
--   • SubscribeClient wires OnClientEvent. Each received batch is
--     walked op-by-op: read header, look up schema for the componentIdx,
--     read field values, call applier(eid, ...fields) (Set) or
--     remover(eid) (Remove).
--   • Latest received tick exposed via GetServerTick.
--
-- Late join:
--   • Shared Ready RemoteEvent. Client fires once on first
--     SubscribeClient; server listens once on first RegisterSnapshotter.
--     On ping, server flips snapshot mode, runs each registered
--     snapshotter (whose QueueSet calls now write to a per-player snap
--     buffer instead of the shared tick buffer), then fires the snap
--     buffer to that one player with FireClient.

local RunService = game:GetService("RunService")
local ReplicatedStorage = game:GetService("ReplicatedStorage")

local EntitiesReplication = {}
EntitiesReplication._suppressCount = 0
EntitiesReplication._isServer = RunService:IsServer()
EntitiesReplication._tickBatches = {}        -- contextName -> batch struct (server)
EntitiesReplication._snapBatches = {}        -- contextName -> batch struct (server, during snapshot)
EntitiesReplication._events = {}             -- contextName -> RemoteEvent
EntitiesReplication._snapshotters = {}       -- contextName -> () -> nil (server-only)
EntitiesReplication._contexts = {}           -- contextName -> true (server: every context we've seen)
EntitiesReplication._ticks = {}              -- contextName -> int (server monotonic counter)
EntitiesReplication._serverTicks = {}        -- contextName -> int (client: highest received)
EntitiesReplication._schemas = {}            -- contextName -> { [compIdx] = {fieldType, ...} }
EntitiesReplication._appliers = {}           -- contextName -> { [compIdx] = applier fn (client) }
EntitiesReplication._removers = {}           -- contextName -> { [compIdx] = remover fn (client) }
EntitiesReplication._heartbeatConnected = false
EntitiesReplication._readyConnected = false  -- server: OnServerEvent wired
EntitiesReplication._readyFired = false      -- client: fired once per session
EntitiesReplication._snapshotMode = false    -- server: routes QueueSet to snap batch during snapshot

-- ----------------------------------------------------------------
-- Field type codes — must match Entities.EntitiesFieldType in C#.
-- ----------------------------------------------------------------

local FT_I32, FT_F32, FT_F64, FT_BOOL, FT_STRING, FT_U8, FT_U16, FT_U32, FT_I8, FT_I16 = 0, 1, 2, 3, 4, 5, 6, 7, 8, 9

local function fieldSize(ft, value)
	if ft == FT_I32 then return 4 end
	if ft == FT_F32 then return 4 end
	if ft == FT_F64 then return 8 end
	if ft == FT_BOOL then return 1 end
	if ft == FT_STRING then return 2 + #value end
	if ft == FT_U8 then return 1 end
	if ft == FT_U16 then return 2 end
	if ft == FT_U32 then return 4 end
	if ft == FT_I8 then return 1 end
	if ft == FT_I16 then return 2 end
	error("EntitiesReplication: unknown field type code " .. tostring(ft))
end

local function writeField(buf, offset, ft, value)
	if ft == FT_I32 then buffer.writei32(buf, offset, value); return offset + 4 end
	if ft == FT_F32 then buffer.writef32(buf, offset, value); return offset + 4 end
	if ft == FT_F64 then buffer.writef64(buf, offset, value); return offset + 8 end
	if ft == FT_BOOL then buffer.writeu8(buf, offset, value and 1 or 0); return offset + 1 end
	if ft == FT_STRING then
		local len = #value
		buffer.writeu16(buf, offset, len)
		buffer.writestring(buf, offset + 2, value)
		return offset + 2 + len
	end
	if ft == FT_U8 then buffer.writeu8(buf, offset, value); return offset + 1 end
	if ft == FT_U16 then buffer.writeu16(buf, offset, value); return offset + 2 end
	if ft == FT_U32 then buffer.writeu32(buf, offset, value); return offset + 4 end
	if ft == FT_I8 then buffer.writei8(buf, offset, value); return offset + 1 end
	if ft == FT_I16 then buffer.writei16(buf, offset, value); return offset + 2 end
	error("EntitiesReplication: unknown field type code " .. tostring(ft))
end

local function readField(buf, offset, ft)
	if ft == FT_I32 then return buffer.readi32(buf, offset), offset + 4 end
	if ft == FT_F32 then return buffer.readf32(buf, offset), offset + 4 end
	if ft == FT_F64 then return buffer.readf64(buf, offset), offset + 8 end
	if ft == FT_BOOL then return buffer.readu8(buf, offset) ~= 0, offset + 1 end
	if ft == FT_STRING then
		local len = buffer.readu16(buf, offset)
		local s = buffer.readstring(buf, offset + 2, len)
		return s, offset + 2 + len
	end
	if ft == FT_U8 then return buffer.readu8(buf, offset), offset + 1 end
	if ft == FT_U16 then return buffer.readu16(buf, offset), offset + 2 end
	if ft == FT_U32 then return buffer.readu32(buf, offset), offset + 4 end
	if ft == FT_I8 then return buffer.readi8(buf, offset), offset + 1 end
	if ft == FT_I16 then return buffer.readi16(buf, offset), offset + 2 end
	error("EntitiesReplication: unknown field type code " .. tostring(ft))
end

-- ----------------------------------------------------------------
-- Batch struct — wraps a growing buffer + write offset + op count.
-- One per context (per side: tick + snap when snapshot is in progress).
-- ----------------------------------------------------------------

local function newBatch()
	return { buf = buffer.create(1024), offset = 0, count = 0 }
end

local function grow(batch, needed)
	local capacity = buffer.len(batch.buf)
	if batch.offset + needed <= capacity then return end
	local newSize = capacity
	while newSize < batch.offset + needed do
		newSize = newSize * 2
	end
	local newBuf = buffer.create(newSize)
	buffer.copy(newBuf, 0, batch.buf, 0, batch.offset)
	batch.buf = newBuf
end

local function appendSet(batch, compIdx, eid, schema, ...)
	-- Size pre-compute: header + per-field size.
	local n = #schema
	local size = 6
	for i = 1, n do
		size = size + fieldSize(schema[i], select(i, ...))
	end
	grow(batch, size)
	buffer.writeu8(batch.buf, batch.offset, 0); batch.offset = batch.offset + 1
	buffer.writeu8(batch.buf, batch.offset, compIdx); batch.offset = batch.offset + 1
	buffer.writei32(batch.buf, batch.offset, eid); batch.offset = batch.offset + 4
	for i = 1, n do
		batch.offset = writeField(batch.buf, batch.offset, schema[i], select(i, ...))
	end
	batch.count = batch.count + 1
end

local function appendRemove(batch, compIdx, eid)
	grow(batch, 6)
	buffer.writeu8(batch.buf, batch.offset, 1); batch.offset = batch.offset + 1
	buffer.writeu8(batch.buf, batch.offset, compIdx); batch.offset = batch.offset + 1
	buffer.writei32(batch.buf, batch.offset, eid); batch.offset = batch.offset + 4
	batch.count = batch.count + 1
end

-- ----------------------------------------------------------------
-- Plugins/Entities folder + per-context RemoteEvents + Ready event.
-- ----------------------------------------------------------------

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

-- ----------------------------------------------------------------
-- Public API — guards + suppress.
-- ----------------------------------------------------------------

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

-- ----------------------------------------------------------------
-- Schema registration — server + client.
-- ----------------------------------------------------------------

function EntitiesReplication.RegisterComponentSchema(contextName, componentIndex, fieldTypes)
	local s = EntitiesReplication._schemas[contextName]
	if s == nil then
		s = {}
		EntitiesReplication._schemas[contextName] = s
	end
	s[componentIndex] = fieldTypes or {}
end

-- ----------------------------------------------------------------
-- Tracked contexts + Heartbeat tick.
-- ----------------------------------------------------------------

local function trackContext(contextName)
	if EntitiesReplication._contexts[contextName] == nil then
		EntitiesReplication._contexts[contextName] = true
		EntitiesReplication._ticks[contextName] = 0
	end
end

local function heartbeatTick()
	for contextName in pairs(EntitiesReplication._contexts) do
		EntitiesReplication._ticks[contextName] = EntitiesReplication._ticks[contextName] + 1

		local batch = EntitiesReplication._tickBatches[contextName]
		if batch ~= nil and batch.count > 0 then
			local ev = getOrCreateEvent(contextName)
			local fire = buffer.create(batch.offset)
			buffer.copy(fire, 0, batch.buf, 0, batch.offset)
			ev:FireAllClients(EntitiesReplication._ticks[contextName], batch.count, fire)
			batch.offset = 0
			batch.count = 0
		end
	end
end

local function ensureHeartbeatHook()
	if EntitiesReplication._heartbeatConnected then return end
	if not EntitiesReplication._isServer then return end
	RunService.Heartbeat:Connect(heartbeatTick)
	EntitiesReplication._heartbeatConnected = true
end

-- ----------------------------------------------------------------
-- QueueSet / QueueRemove — server-side pack.
-- ----------------------------------------------------------------

-- Routes the QueueSet/QueueRemove write to either the tick batch or the
-- per-player snapshot batch depending on _snapshotMode. Pre-warms tick
-- state lazily on first QueueSet.
local function activeBatchFor(contextName)
	if EntitiesReplication._snapshotMode then
		local b = EntitiesReplication._snapBatches[contextName]
		if b == nil then
			b = newBatch()
			EntitiesReplication._snapBatches[contextName] = b
		end
		return b
	end
	local b = EntitiesReplication._tickBatches[contextName]
	if b == nil then
		b = newBatch()
		EntitiesReplication._tickBatches[contextName] = b
		getOrCreateEvent(contextName)
		trackContext(contextName)
		ensureHeartbeatHook()
	end
	return b
end

function EntitiesReplication.QueueSet(contextName, componentIndex, entityId, ...)
	if not EntitiesReplication._isServer then return end
	local ctxSchemas = EntitiesReplication._schemas[contextName]
	local schema = ctxSchemas and ctxSchemas[componentIndex]
	if schema == nil then
		error("EntitiesReplication.QueueSet: no schema for context '" .. contextName
			.. "' componentIndex " .. tostring(componentIndex)
			.. " — did {Ctx}ServerReplication run RegisterComponentSchema?")
	end
	appendSet(activeBatchFor(contextName), componentIndex, entityId, schema, ...)
end

function EntitiesReplication.QueueRemove(contextName, componentIndex, entityId)
	if not EntitiesReplication._isServer then return end
	appendRemove(activeBatchFor(contextName), componentIndex, entityId)
end

-- ----------------------------------------------------------------
-- Snapshot (server, called per Ready ping).
-- ----------------------------------------------------------------

local function ensureReadyListener()
	if EntitiesReplication._readyConnected then return end
	if not EntitiesReplication._isServer then return end
	EntitiesReplication._readyConnected = true
	local readyEv = getOrCreateReadyEvent()
	readyEv.OnServerEvent:Connect(function(player)
		for contextName, snapshotFn in pairs(EntitiesReplication._snapshotters) do
			-- Per-context per-player snap batch. The snapshotter's
			-- QueueSet calls land here while _snapshotMode is true.
			EntitiesReplication._snapBatches[contextName] = newBatch()
			EntitiesReplication._snapshotMode = true
			local ok, err = pcall(snapshotFn)
			EntitiesReplication._snapshotMode = false

			local batch = EntitiesReplication._snapBatches[contextName]
			EntitiesReplication._snapBatches[contextName] = nil

			if not ok then
				warn("EntitiesReplication: snapshotter for context '" .. contextName .. "' errored: " .. tostring(err))
			elseif batch.count > 0 then
				local fire = buffer.create(batch.offset)
				buffer.copy(fire, 0, batch.buf, 0, batch.offset)
				getOrCreateEvent(contextName):FireClient(player, EntitiesReplication._ticks[contextName] or 0, batch.count, fire)
			end
		end
	end)
end

function EntitiesReplication.RegisterSnapshotter(contextName, snapshot)
	if not EntitiesReplication._isServer then return end
	EntitiesReplication._snapshotters[contextName] = snapshot
	trackContext(contextName)
	getOrCreateEvent(contextName)
	ensureReadyListener()
	ensureHeartbeatHook()
end

-- ----------------------------------------------------------------
-- Client appliers/removers + subscribe.
-- ----------------------------------------------------------------

function EntitiesReplication.RegisterClientApplier(contextName, componentIndex, applier)
	local a = EntitiesReplication._appliers[contextName]
	if a == nil then
		a = {}
		EntitiesReplication._appliers[contextName] = a
	end
	a[componentIndex] = applier
end

function EntitiesReplication.RegisterClientRemover(contextName, componentIndex, remover)
	local r = EntitiesReplication._removers[contextName]
	if r == nil then
		r = {}
		EntitiesReplication._removers[contextName] = r
	end
	r[componentIndex] = remover
end

-- Walks a received buffer, dispatches each op to the registered applier
-- (Set) or remover (Remove) for that context. Single self:context()
-- per applier call; field values read inline from the buffer using the
-- schema. Unknown componentIndex or missing applier/remover skips the
-- op (logged once would be nicer; deferred).
local function applyBatch(contextName, count, buf)
	local schemas = EntitiesReplication._schemas[contextName] or {}
	local appliers = EntitiesReplication._appliers[contextName] or {}
	local removers = EntitiesReplication._removers[contextName] or {}
	local offset = 0
	for i = 1, count do
		local opcode = buffer.readu8(buf, offset); offset = offset + 1
		local compIdx = buffer.readu8(buf, offset); offset = offset + 1
		local eid = buffer.readi32(buf, offset); offset = offset + 4
		if opcode == 1 then
			local rm = removers[compIdx]
			if rm ~= nil then rm(eid) end
		else
			local schema = schemas[compIdx]
			local applier = appliers[compIdx]
			if applier ~= nil then
				if schema == nil or #schema == 0 then
					applier(eid)
				elseif #schema == 1 then
					local v1
					v1, offset = readField(buf, offset, schema[1])
					applier(eid, v1)
				elseif #schema == 2 then
					local v1, v2
					v1, offset = readField(buf, offset, schema[1])
					v2, offset = readField(buf, offset, schema[2])
					applier(eid, v1, v2)
				elseif #schema == 3 then
					local v1, v2, v3
					v1, offset = readField(buf, offset, schema[1])
					v2, offset = readField(buf, offset, schema[2])
					v3, offset = readField(buf, offset, schema[3])
					applier(eid, v1, v2, v3)
				else
					-- General varargs fallback. Allocates a small table,
					-- accepted cost for >3-field components (rare in
					-- typical Entitas-shaped data).
					local args = table.create(#schema + 1)
					args[1] = eid
					for j = 1, #schema do
						local v
						v, offset = readField(buf, offset, schema[j])
						args[j + 1] = v
					end
					applier(table.unpack(args))
				end
			else
				-- No applier registered → still need to advance offset so
				-- subsequent ops parse correctly.
				if schema ~= nil then
					for j = 1, #schema do
						_, offset = readField(buf, offset, schema[j])
					end
				end
			end
		end
	end
end

function EntitiesReplication.SubscribeClient(contextName)
	if EntitiesReplication._isServer then return end
	local ev = getOrCreateEvent(contextName)
	ev.OnClientEvent:Connect(function(tick, count, buf)
		local prev = EntitiesReplication._serverTicks[contextName] or 0
		if tick ~= nil and tick > prev then
			EntitiesReplication._serverTicks[contextName] = tick
		end
		applyBatch(contextName, count, buf)
	end)
	if not EntitiesReplication._readyFired then
		EntitiesReplication._readyFired = true
		getOrCreateReadyEvent():FireServer()
	end
end

-- ----------------------------------------------------------------
-- Tick accessors.
-- ----------------------------------------------------------------

function EntitiesReplication.GetTick(contextName)
	if not EntitiesReplication._isServer then return 0 end
	return EntitiesReplication._ticks[contextName] or 0
end

function EntitiesReplication.GetServerTick(contextName)
	if EntitiesReplication._isServer then return 0 end
	return EntitiesReplication._serverTicks[contextName] or 0
end

return EntitiesReplication
