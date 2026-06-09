-- Time-travel snapshot ring. Three modes:
--   "off"   — no recording (default, zero cost).
--   "delta" — record only entities whose NotifyComponentChanged fires this
--             tick. Cheap; matches "follow one entity through time" usage.
--   "full"  — record every entity in every attached context, every tick.
--             Expensive: O(entities × components) shallow copies per tick.
--             Toggled explicitly by the UI Record button.
--
-- Frames key by entity:creationIndex() (an int that's monotonic per
-- context's lifetime), not by entity table identity, so the Context
-- _reusableEntities pool can recycle the same table for a new logical
-- entity without aliasing an old snapshot's reference into a new meaning.

local DebuggerSnapshot = {}
DebuggerSnapshot.__index = DebuggerSnapshot

local DEFAULT_RING_SIZE = 300

local MODE_OFF = "off"
local MODE_DELTA = "delta"
local MODE_FULL = "full"

function DebuggerSnapshot.new()
	local self = setmetatable({}, DebuggerSnapshot)
	self._mode = MODE_OFF
	self._ringSize = DEFAULT_RING_SIZE
	self._ring = {}            -- [ringIdx] = { tick, byCreationIndex }
	self._ringHead = 1         -- next write slot (1-based)
	self._frameCount = 0       -- ≤ ringSize; tracks fill before first wrap
	self._currentFrame = nil   -- in-progress frame between BeginFrame/EndFrame
	return self
end

function DebuggerSnapshot:SetMode(mode)
	assert(mode == MODE_OFF or mode == MODE_DELTA or mode == MODE_FULL,
		"Snapshot mode must be off|delta|full")
	self._mode = mode
end

function DebuggerSnapshot:GetMode() return self._mode end
function DebuggerSnapshot:SetRingSize(n) self._ringSize = n end
function DebuggerSnapshot:FrameCount() return self._frameCount end

-- Shallow copy. Components are POD-shaped (transpiled C# fields land as
-- direct table keys), so one level of {k=v for k,v} captures the visible
-- state. Roblox Instance refs in components are stored as-is (intentional
-- — deep-cloning a Workspace ref would be both wrong and very expensive).
local function shallowCopy(t)
	if type(t) ~= "table" then return t end
	local out = {}
	for k, v in pairs(t) do out[k] = v end
	return out
end

function DebuggerSnapshot:BeginFrame(tick)
	if self._mode == MODE_OFF then return end
	self._currentFrame = { tick = tick, byCreationIndex = {} }
end

-- Captures one entity. Called per-mutation in delta mode (from the
-- Context._debugListeners hook), or once-per-entity-per-tick in full mode
-- (from Debugger:_tick's full-mode walk).
function DebuggerSnapshot:RecordEntity(entity)
	if self._mode == MODE_OFF or self._currentFrame == nil then return end
	local indices = entity:GetComponentIndices()
	local snap = {}
	for i = 1, #indices do
		local idx = indices[i]
		snap[idx] = shallowCopy(entity:GetComponent(idx))
	end
	self._currentFrame.byCreationIndex[entity:creationIndex()] = {
		components = snap,
		isEnabled = entity:isEnabled(),
	}
end

function DebuggerSnapshot:EndFrame()
	if self._mode == MODE_OFF or self._currentFrame == nil then return end
	self._ring[self._ringHead] = self._currentFrame
	self._ringHead = (self._ringHead % self._ringSize) + 1
	if self._frameCount < self._ringSize then
		self._frameCount = self._frameCount + 1
	end
	self._currentFrame = nil
end

-- Read a snapshot for `creationIndex` at `offset` frames back from newest.
-- offset 0 = newest frame, 1 = previous tick, etc. Returns (entitySnap, tick)
-- or (nil, nil) if out of range or never recorded.
function DebuggerSnapshot:Get(creationIndex, offset)
	if self._frameCount == 0 then return nil, nil end
	offset = offset or 0
	if offset >= self._frameCount then return nil, nil end
	-- ringHead points at next-write slot; newest frame sits at ringHead-1
	-- (1-based, wrap-aware).
	local idx = ((self._ringHead - 2 - offset) % self._ringSize) + 1
	local frame = self._ring[idx]
	if frame == nil then return nil, nil end
	return frame.byCreationIndex[creationIndex], frame.tick
end

return DebuggerSnapshot
