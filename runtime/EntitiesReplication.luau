-- Replication wire. Single RemoteEvent per context (lives at
-- ReplicatedStorage.Plugins.Entities.<Context>Replication). Every fire
-- carries a monotonic server tick + an array of ops:
--   ev:FireAllClients(tick, ops)
--   Set:    {0, componentIndex, entityId, bitmask, ...changedFields}
--   Remove: {1, componentIndex, entityId}
-- Bitmask is a Lua number; bit i-1 set means field i is present in the
-- changedFields tail (in field-declaration order). For a flag component
-- the bitmask is 0 and no fields follow. Client distinguishes Add vs
-- Replace by HasX. See QueueSet for the diff/baseline mechanics.
--
-- Server:
--   • Per-context monotonic tick increments on every Heartbeat
--     regardless of activity (so the value reflects wall-clock progress,
--     not just dirty batches).
--   • Codegen-emitted entity AddX/ReplaceX/RemoveX bodies enqueue via
--     QueueSet / QueueRemove.
--   • Heartbeat tail drains every context's buffer at end of frame and
--     fires the per-context RemoteEvent once with the whole batch. Large
--     batches are split into CHUNK_SIZE-op chunks so a single fire stays
--     under Roblox's RemoteEvent payload limit; Roblox preserves per-
--     RemoteEvent ordering across fires, so each chunk applies cleanly.
--
-- Client:
--   • ShouldEmit is always false, so codegen-emitted bodies skip the
--     enqueue. The codegen-emitted {Ctx}ClientReplication subscribes once
--     via Subscribe(name, handler); the runtime wraps the handler to
--     track latest received tick before dispatching ops.
--   • Latest received tick per context is exposed via GetServerTick.
--   • BeginSuppress / EndSuppress are counter-based so nested suppress
--     scopes compose — reserved for a future client→server flow; the
--     current alpha is server-authoritative only.
--
-- Late join:
--   • A shared Ready RemoteEvent (also under Plugins.Entities) carries a
--     client→server "I'm subscribed, send me the world" ping per context,
--     tagged with the client's BuildDigest. The client fires it exactly
--     once per context on first Subscribe. Server compares against the
--     digest registered via RegisterDigest; mismatch kicks the player
--     (the only safe response — componentIndex would map to a different
--     type than the server expects).
--   • On digest match the (player, contextName) pair is queued for the
--     next heartbeat. The heartbeat drains the per-frame buffer FIRST
--     (so the joining player receives this frame's batch like everyone
--     else), then walks the queue and FireClients the snapshot ops
--     batch directly to that one player on the per-context RemoteEvent,
--     tagged with the post-drain server tick. Same RemoteEvent, same
--     tick — Roblox preserves per-event ordering, so the client sees
--     broadcast batch → snapshot. No race window where a Remove in
--     the batch could undo a value present in the snapshot.
--   • ApplySet on the client picks Add-vs-Replace by HasX so any tick-
--     time op that races the snapshot stays idempotent — and pagination
--     plays nicely with the racing concern because order is preserved.

local RunService = game:GetService("RunService")
local ReplicatedStorage = game:GetService("ReplicatedStorage")

-- Max ops per single RemoteEvent fire. ~200 ops × ~50 bytes each keeps a
-- chunk under ~10KB, well below Roblox's per-event soft limit. Tune if
-- you're seeing fragmentation in profiling.
local CHUNK_SIZE = 200

local EntitiesReplication = {}
EntitiesReplication._suppressCount = 0
EntitiesReplication._isServer = RunService:IsServer()
EntitiesReplication._buffers = {}            -- contextName -> array of ops (server)
EntitiesReplication._events = {}             -- contextName -> RemoteEvent
EntitiesReplication._snapshotters = {}       -- contextName -> () -> ops array (server)
EntitiesReplication._contexts = {}           -- contextName -> true (server: every context we've seen)
EntitiesReplication._ticks = {}              -- contextName -> int (server monotonic counter)
EntitiesReplication._serverTicks = {}        -- contextName -> int (client: highest received)
EntitiesReplication._heartbeatConnected = false
EntitiesReplication._readyConnected = false  -- server: OnServerEvent wired
EntitiesReplication._readyFiredFor = {}      -- client: contextName -> true (fired once per context)
EntitiesReplication._digests = {}            -- contextName -> string (server: expected client digest)
EntitiesReplication._pendingSnapshots = {}   -- server: array of {player, contextName} queued by Ready ping, drained next heartbeat
EntitiesReplication._baselines = {}          -- server: contextName -> entityId -> componentIndex -> {lastSentField1, lastSentField2, ...}

-- Command wire (client → server). Separate RemoteEvent per context lives
-- at Plugins.Entities.<Context>Command. Op shape is intentionally simpler
-- than the server→client Set wire because each command spawns a fresh
-- server-side entity — there's no baseline to diff against, so we don't
-- carry a bitmask. Every field rides explicitly.
--   command op = {componentIndex, clientLocalId, ...allFields}
-- The client batches per heartbeat by clientLocalId — all ops sharing the
-- same id arrive together and land on one server-side entity. clientLocalId
-- is just a correlation key within a single FireServer batch; the server
-- doesn't persist it.
--
-- Trigger model: user sets `entity.IsCommand = true`. The codegen-emitted
-- setter tails MarkCommandPending; the heartbeat drain re-checks
-- IsCommand on each pending entity (so a same-frame toggle off cancels
-- cleanly), then invokes the per-context shipper which walks the entity's
-- component indices and emits one op per component. The Command flag
-- itself rides — the server-spawned entity carries it too so
-- AllOf<Command, ...> works on both sides.
EntitiesReplication._commandEvents = {}      -- contextName -> RemoteEvent (per side)
EntitiesReplication._commandReceivers = {}   -- server: contextName -> (player, ops) -> nil
EntitiesReplication._commandDigests = {}     -- client: contextName -> BuildDigest (forwarded with every fire)
EntitiesReplication._commandShippers = {}    -- client: contextName -> (entity, outOps) -> nil (codegen-registered)
EntitiesReplication._commandPending = {}     -- client: contextName -> array of entities awaiting ship
EntitiesReplication._commandPendingSet = {}  -- client: contextName -> entity -> true (dedupe set; mirrors _commandPending)
EntitiesReplication._commandHeartbeatConnected = false  -- client: hook installed flag

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

-- Track every context we've seen on the server so the Heartbeat tick can
-- advance its counter even before a snapshotter is registered or before
-- the first QueueSet fires.
local function trackContext(contextName)
	if EntitiesReplication._contexts[contextName] == nil then
		EntitiesReplication._contexts[contextName] = true
		EntitiesReplication._ticks[contextName] = 0
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

-- Chunk an ops array, calling `fn` with each chunk. Single-fire path
-- short-circuits the chunking allocation. Caller-side `fn` decides the
-- fire shape (FireAllClients for tick batches, FireClient for snapshot).
local function chunked(ops, fn)
	local n = #ops
	if n <= CHUNK_SIZE then
		fn(ops)
		return
	end
	local i = 1
	while i <= n do
		local chunk = {}
		local last = math.min(i + CHUNK_SIZE - 1, n)
		for k = i, last do
			table.insert(chunk, ops[k])
		end
		fn(chunk)
		i = last + 1
	end
end

-- Server-side per-Heartbeat: tick every tracked context, drain any
-- non-empty buffers, then send late-join snapshots to players queued
-- since the previous heartbeat. Snapshots are deferred to here (rather
-- than fired inline from OnServerEvent) so a joining player can't
-- receive a snapshot built post-mutation racing a FireAllClients batch
-- that contains the pre-snapshot ops — both come from the same tick on
-- the same RemoteEvent, ordered: broadcast first, snapshot second.
-- Tick advances even when nothing fires so the value reflects wall-
-- clock progress; client only learns about ticks via the fires it
-- actually receives, so quiet contexts leave the client's serverTick
-- frozen — that's a feature, not a bug.
local function heartbeatTick()
	for contextName in pairs(EntitiesReplication._contexts) do
		EntitiesReplication._ticks[contextName] = EntitiesReplication._ticks[contextName] + 1

		local buf = EntitiesReplication._buffers[contextName]
		if buf ~= nil and #buf > 0 then
			local ev = getOrCreateEvent(contextName)
			local tick = EntitiesReplication._ticks[contextName]
			chunked(buf, function(chunk)
				ev:FireAllClients(tick, chunk)
			end)
			EntitiesReplication._buffers[contextName] = {}
		end
	end

	-- Snapshot phase. Pending players were queued by the Ready event
	-- handler at any point during the frame; drain them now so each
	-- player gets a snapshot tagged with the post-drain tick. The
	-- batch above has already flown for the current frame, so the
	-- joining player sees: broadcast (frame N's diffs to everyone) →
	-- snapshot (state after frame N's commits). No room for a Remove
	-- in the batch to undo a value present in the snapshot, and vice
	-- versa.
	local pending = EntitiesReplication._pendingSnapshots
	if #pending > 0 then
		EntitiesReplication._pendingSnapshots = {}
		for i = 1, #pending do
			local entry = pending[i]
			local player = entry.player
			local contextName = entry.contextName
			local snapshotFn = EntitiesReplication._snapshotters[contextName]
			if snapshotFn ~= nil then
				local ok, ops = pcall(snapshotFn)
				if not ok then
					warn("EntitiesReplication: snapshotter for context '" .. contextName .. "' errored: " .. tostring(ops))
				elseif ops ~= nil and #ops > 0 then
					local ctxEv = getOrCreateEvent(contextName)
					local tick = EntitiesReplication._ticks[contextName] or 0
					chunked(ops, function(chunk)
						ctxEv:FireClient(player, tick, chunk)
					end)
				end
			end
		end
	end
end

local function ensureHeartbeatHook()
	if EntitiesReplication._heartbeatConnected then return end
	if not EntitiesReplication._isServer then return end
	RunService.Heartbeat:Connect(heartbeatTick)
	EntitiesReplication._heartbeatConnected = true
end

local function ensureReadyListener()
	if EntitiesReplication._readyConnected then return end
	if not EntitiesReplication._isServer then return end
	EntitiesReplication._readyConnected = true
	local readyEv = getOrCreateReadyEvent()
	-- Validate digest, then queue (player, contextName) for the next
	-- heartbeat's snapshot drain. Digest is the codegen-stamped fingerprint
	-- of the sorted component layout for this context — a mismatch means
	-- the client's componentIndex ↔ type mapping disagrees with the
	-- server's, so every Set op routed by index would land on the wrong
	-- component. Kicking the player is the only safe response; let them
	-- restart with the current client build.
	readyEv.OnServerEvent:Connect(function(player, contextName, clientDigest)
		local expected = EntitiesReplication._digests[contextName]
		if expected ~= nil and clientDigest ~= expected then
			player:Kick(
				"[Entities] Context '" .. tostring(contextName) ..
				"' build digest mismatch (client=" .. tostring(clientDigest) ..
				", server=" .. tostring(expected) ..
				"). Update your client.")
			return
		end
		table.insert(EntitiesReplication._pendingSnapshots, {
			player = player,
			contextName = contextName,
		})
	end)
end

local function bufferFor(contextName)
	local buf = EntitiesReplication._buffers[contextName]
	if buf == nil then
		buf = {}
		EntitiesReplication._buffers[contextName] = buf
		-- Pre-warm the RemoteEvent + heartbeat hook lazily on first use.
		getOrCreateEvent(contextName)
		trackContext(contextName)
		ensureHeartbeatHook()
	end
	return buf
end

-- Set op (Add + Replace collapse to one wire shape; client distinguishes
-- by HasX). Variadic over the field values — the C# stub exposes 0..6
-- explicit-arity overloads, all of which lower to this single function.
--
-- Delta encoding: we keep a per-(context, entity, component) baseline of
-- the field values we last broadcast. On each call we diff incoming
-- fields against the baseline, build a bitmask of changed positions
-- (field i → bit i-1), and ship only those positions on the wire. The
-- client reconstructs the full tuple from its local component for the
-- positions the bitmask zeros out.
--   • prior nil (first Set, or after a Remove) → all-ones bitmask, every
--     field sent. Establishes the baseline.
--   • bitmask = 0 (every field equals baseline) → idempotent skip. Saves
--     bandwidth on reactive systems that re-fire Replace{X} with the
--     same values.
--   • flag components (fieldCount == 0) skip the bitmask path entirely;
--     prior-nil emits the op, prior-present skips it.
-- 32-field cap on multi-field components (single Lua-number bitmask) —
-- not enforced; if you push past that the math.floor(2^32) starts losing
-- precision and you'll see spurious "unchanged" reads.
function EntitiesReplication.QueueSet(contextName, componentIndex, entityId, ...)
	if not EntitiesReplication._isServer then return end

	local fieldCount = select("#", ...)

	local ctxBase = EntitiesReplication._baselines[contextName]
	if ctxBase == nil then
		ctxBase = {}
		EntitiesReplication._baselines[contextName] = ctxBase
	end
	local entBase = ctxBase[entityId]
	if entBase == nil then
		entBase = {}
		ctxBase[entityId] = entBase
	end
	local prior = entBase[componentIndex]

	if fieldCount == 0 then
		-- Flag: nothing to diff. Skip if the wire already carries this
		-- flag-presence; emit + sentinel if not.
		if prior ~= nil then return end
		entBase[componentIndex] = {}
		table.insert(bufferFor(contextName), { 0, componentIndex, entityId, 0 })
		return
	end

	local newFields = { ... }
	local bitmask = 0
	local delta = {}

	if prior == nil then
		-- First send for this component-on-entity. Every field is "new".
		for i = 1, fieldCount do
			bitmask = bit32.bor(bitmask, bit32.lshift(1, i - 1))
			delta[i] = newFields[i]
		end
	else
		local deltaIdx = 0
		for i = 1, fieldCount do
			if prior[i] ~= newFields[i] then
				bitmask = bit32.bor(bitmask, bit32.lshift(1, i - 1))
				deltaIdx = deltaIdx + 1
				delta[deltaIdx] = newFields[i]
			end
		end
		if bitmask == 0 then return end
	end

	entBase[componentIndex] = newFields

	local op = { 0, componentIndex, entityId, bitmask }
	for i = 1, #delta do
		op[#op + 1] = delta[i]
	end
	table.insert(bufferFor(contextName), op)
end

function EntitiesReplication.QueueRemove(contextName, componentIndex, entityId)
	if not EntitiesReplication._isServer then return end
	-- Drop the baseline so a future re-add starts fresh (all-ones
	-- bitmask). Also collapse the entity-level table when it empties out
	-- so a long-running game doesn't accumulate stale entityId keys for
	-- entities that have been fully torn down.
	local ctxBase = EntitiesReplication._baselines[contextName]
	if ctxBase ~= nil then
		local entBase = ctxBase[entityId]
		if entBase ~= nil then
			entBase[componentIndex] = nil
			if next(entBase) == nil then
				ctxBase[entityId] = nil
			end
		end
	end
	table.insert(bufferFor(contextName), { 1, componentIndex, entityId })
end

-- Client-side single subscription point. The codegen-emitted
-- {Ctx}ClientReplication ctor calls this exactly once per context with the
-- locally-known BuildDigest and a handler `(ops) -> nil`. We wrap to also
-- update `_serverTicks` from the payload's tick field before dispatching
-- the ops array. First call per context also pings the server's Ready
-- event with (contextName, digest) so the server can validate the
-- handshake and dispatch a late-join snapshot back; subsequent Subscribes
-- on the same context don't re-ping.
function EntitiesReplication.Subscribe(contextName, buildDigest, handler)
	if EntitiesReplication._isServer then return end
	local ev = getOrCreateEvent(contextName)
	ev.OnClientEvent:Connect(function(tick, ops)
		local prev = EntitiesReplication._serverTicks[contextName] or 0
		if tick ~= nil and tick > prev then
			EntitiesReplication._serverTicks[contextName] = tick
		end
		handler(ops)
	end)
	if EntitiesReplication._readyFiredFor[contextName] == nil then
		EntitiesReplication._readyFiredFor[contextName] = true
		local readyEv = getOrCreateReadyEvent()
		readyEv:FireServer(contextName, buildDigest)
	end
end

-- Server-only. Registers the expected client BuildDigest for a context.
-- Stored for comparison in the Ready handler — see ensureReadyListener.
-- Codegen-emitted {Ctx}ServerReplication ctor calls this once per context
-- with {Ctx}ComponentsLookup.BuildDigest.
function EntitiesReplication.RegisterDigest(contextName, buildDigest)
	if not EntitiesReplication._isServer then return end
	EntitiesReplication._digests[contextName] = buildDigest
end

-- Server-only. The codegen-emitted {Ctx}ServerReplication ctor registers
-- a snapshotter once per context. The fn is called per joining player
-- and should return the ops array for the whole world right now.
function EntitiesReplication.RegisterSnapshotter(contextName, snapshot)
	if not EntitiesReplication._isServer then return end
	EntitiesReplication._snapshotters[contextName] = snapshot
	trackContext(contextName)
	-- Pre-warm the context's RemoteEvent + Ready listener so the first
	-- joining client has somewhere to fire and somewhere to be heard.
	getOrCreateEvent(contextName)
	ensureReadyListener()
	-- Also wire the Heartbeat tick — even a snapshot-only context wants
	-- its tick to advance for the wall-clock semantics.
	ensureHeartbeatHook()
end

-- Server: current monotonic tick. Client call returns 0 (no concept of
-- "server tick" exists locally; use GetServerTick instead).
function EntitiesReplication.GetTick(contextName)
	if not EntitiesReplication._isServer then return 0 end
	return EntitiesReplication._ticks[contextName] or 0
end

-- Client: highest server tick observed so far on this context.
function EntitiesReplication.GetServerTick(contextName)
	if EntitiesReplication._isServer then return 0 end
	return EntitiesReplication._serverTicks[contextName] or 0
end

-- ---------------------------------------------------------------------
-- Command wire (client → server).
-- ---------------------------------------------------------------------

local function getOrCreateCommandEvent(contextName)
	local existing = EntitiesReplication._commandEvents[contextName]
	if existing ~= nil then return existing end

	local parent = pluginsFolder()
	local eventName = contextName .. "Command"

	if EntitiesReplication._isServer then
		local inst = parent:FindFirstChild(eventName)
		if inst == nil then
			inst = Instance.new("RemoteEvent")
			inst.Name = eventName
			inst.Parent = parent
		end
		EntitiesReplication._commandEvents[contextName] = inst
		return inst
	else
		local inst = parent:WaitForChild(eventName)
		EntitiesReplication._commandEvents[contextName] = inst
		return inst
	end
end

-- Client-side per-Heartbeat drain. Walks every context with pending
-- entities, invokes the registered shipper to build the op batch (one
-- op per component on each entity), and fires with the locally-known
-- BuildDigest as the leading arg (server re-validates every fire —
-- Ready alone doesn't establish a trusted session, so the digest needs
-- to ride each batch). After firing, the entity stays in the user's
-- world; user-side cleanup (typically a CommandHandlerSystem on the
-- client mirror, or just a one-frame entity factory) handles teardown.
local function commandHeartbeatDrain()
	for contextName, pending in pairs(EntitiesReplication._commandPending) do
		if #pending > 0 then
			local shipper = EntitiesReplication._commandShippers[contextName]
			local digest = EntitiesReplication._commandDigests[contextName]
			if shipper ~= nil then
				local ops = {}
				for i = 1, #pending do
					local entity = pending[i]
					-- Re-check: user could have toggled IsCommand off
					-- post-Mark, or destroyed the entity entirely.
					-- Skipping here keeps the ship cancellable up to
					-- the heartbeat boundary.
					if entity:isEnabled() then
						shipper(entity, ops)
					end
				end
				if #ops > 0 then
					local ev = getOrCreateCommandEvent(contextName)
					chunked(ops, function(chunk)
						ev:FireServer(digest, chunk)
					end)
				end
			end
			EntitiesReplication._commandPending[contextName] = {}
			EntitiesReplication._commandPendingSet[contextName] = {}
		end
	end
end

local function ensureCommandHeartbeatHook()
	if EntitiesReplication._commandHeartbeatConnected then return end
	if EntitiesReplication._isServer then return end
	RunService.Heartbeat:Connect(commandHeartbeatDrain)
	EntitiesReplication._commandHeartbeatConnected = true
end

function EntitiesReplication.ShouldSendCommand()
	return not EntitiesReplication._isServer
end

-- Client trigger. Codegen-emitted setter for the synthesized Command
-- flag's true branch calls this. We dedupe via the per-context set so
-- repeated IsCommand=true within one frame don't double-ship; insertion
-- order in _commandPending defines the heartbeat-drain order.
function EntitiesReplication.MarkCommandPending(contextName, entity)
	if EntitiesReplication._isServer then return end
	local set = EntitiesReplication._commandPendingSet[contextName]
	if set == nil then
		set = {}
		EntitiesReplication._commandPendingSet[contextName] = set
	end
	if set[entity] then return end
	set[entity] = true

	local pending = EntitiesReplication._commandPending[contextName]
	if pending == nil then
		pending = {}
		EntitiesReplication._commandPending[contextName] = pending
	end
	table.insert(pending, entity)
	ensureCommandHeartbeatHook()
end

-- False-branch counterpart. Toggling IsCommand off pre-drain cancels
-- the pending ship cleanly. After a drain the entity gets re-added
-- naturally if IsCommand flips back on next frame.
function EntitiesReplication.UnmarkCommandPending(contextName, entity)
	if EntitiesReplication._isServer then return end
	local set = EntitiesReplication._commandPendingSet[contextName]
	if set == nil or not set[entity] then return end
	set[entity] = nil

	local pending = EntitiesReplication._commandPending[contextName]
	if pending == nil then return end
	for i = #pending, 1, -1 do
		if pending[i] == entity then
			table.remove(pending, i)
			return
		end
	end
end

-- Client setup. Codegen-emitted {Ctx}ClientCommandSender ctor calls
-- this once per context with the locally-known BuildDigest and a
-- shipper fn. The shipper walks the entity's components and pushes
-- one op per component into the supplied list. We hold both for the
-- heartbeat drain to invoke; the digest also rides the wire so the
-- server can validate per-fire (Ready handshake alone isn't enough
-- for command authorization — first commands can race the Ready ping).
function EntitiesReplication.ConfigureCommandSender(contextName, buildDigest, shipper)
	if EntitiesReplication._isServer then return end
	EntitiesReplication._commandDigests[contextName] = buildDigest
	EntitiesReplication._commandShippers[contextName] = shipper
	getOrCreateCommandEvent(contextName)
	ensureCommandHeartbeatHook()
end

-- Server setup. Codegen-emitted {Ctx}ServerCommandReceiver ctor calls
-- this with a handler `(player, ops) -> nil`. The runtime wires a
-- OnServerEvent listener that validates the per-fire BuildDigest against
-- the context's expected digest (registered via RegisterDigest) and
-- kicks the player on mismatch — same hard-fail safeguard as the Ready
-- ping. On match it dispatches ops to the user handler, which decodes
-- them, spawns entities, and attaches OriginUserId from the Player.
function EntitiesReplication.RegisterCommandReceiver(contextName, receiver)
	if not EntitiesReplication._isServer then return end
	EntitiesReplication._commandReceivers[contextName] = receiver
	local ev = getOrCreateCommandEvent(contextName)
	ev.OnServerEvent:Connect(function(player, clientDigest, ops)
		local expected = EntitiesReplication._digests[contextName]
		if expected ~= nil and clientDigest ~= expected then
			player:Kick(
				"[Entities] Context '" .. tostring(contextName) ..
				"' command digest mismatch (client=" .. tostring(clientDigest) ..
				", server=" .. tostring(expected) ..
				"). Update your client.")
			return
		end
		receiver(player, ops)
	end)
end

return EntitiesReplication
