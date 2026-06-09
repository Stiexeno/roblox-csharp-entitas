-- Per-system rolling-average timing + skip set + error capture.
-- A profiler is created per feature-group at Debugger:AutoInitialize and
-- attached to each Feature in that group via Systems:SetProfiler(fn).
-- The fn closure captures the profiler instance; Systems calls it once
-- per system per phase, the profiler times the inner system[phase] call
-- and writes into a per-(system, phase) ring buffer.
--
-- skipSystems[system] = true short-circuits the dispatch so the system
-- doesn't run that tick — driven from the UI right-click "disable system"
-- affordance, matching Matter's Loop._skipSystems convention.

local RollingAverage = require(script.Parent.RollingAverage)

local DebuggerProfiler = {}
DebuggerProfiler.__index = DebuggerProfiler

function DebuggerProfiler.new()
	local self = setmetatable({}, DebuggerProfiler)
	self._samples = {}        -- [system] = { [phaseName] = rolling sample table }
	self._skip = {}           -- [system] = true → don't run
	self._errors = {}         -- [system] = lastError string (cleared on next clean tick)
	return self
end

function DebuggerProfiler:Samples() return self._samples end
function DebuggerProfiler:SkipSet() return self._skip end
function DebuggerProfiler:Errors() return self._errors end

function DebuggerProfiler:GetAverage(system, phaseName)
	local perSystem = self._samples[system]
	if perSystem == nil then return 0 end
	local samples = perSystem[phaseName]
	if samples == nil or #samples == 0 then return 0 end
	return RollingAverage.getAverage(samples)
end

function DebuggerProfiler:Profile(system, phaseName)
	if self._skip[system] then return end

	local t0 = os.clock()
	local ok, err = pcall(function()
		system[phaseName](system)
	end)
	local dt = os.clock() - t0

	if ok then
		self._errors[system] = nil
	else
		self._errors[system] = tostring(err)
	end

	local perSystem = self._samples[system]
	if perSystem == nil then
		perSystem = {}
		self._samples[system] = perSystem
	end
	local samples = perSystem[phaseName]
	if samples == nil then
		samples = {}
		perSystem[phaseName] = samples
	end
	RollingAverage.addSample(samples, dt)
end

-- Returns a closure bound to this profiler. Assign the result to
-- `Systems._profiler` (via SetProfiler) so the dispatch path calls into
-- DebuggerProfiler:Profile for every (system, phase).
function DebuggerProfiler:Bind()
	return function(system, phaseName)
		self:Profile(system, phaseName)
	end
end

return DebuggerProfiler
