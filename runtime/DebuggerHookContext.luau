-- Monkey-patch Group:GetEntities to record per-system group access for
-- the QueryInspect widget. Matter's hookWorld wraps World.query the same
-- way. We use a STACK of currentDebugSystem so nested
--   for _, e in groupA do for _, e2 in groupB do … end end
-- across systems attributes each iteration correctly.
--
-- Hook/Unhook are called by Debugger:_enable / :_disable. While
-- unhooked, Group:GetEntities runs at original cost.

local Group = require(script.Parent.Group)
local RollingAverage = require(script.Parent.RollingAverage)

local DebuggerHookContext = {}

local originalGetEntities = Group.GetEntities
local hookedDebugger = nil

function DebuggerHookContext.PushDebugSystem(debugger, system)
	table.insert(debugger._currentDebugSystemStack, system)
end

function DebuggerHookContext.PopDebugSystem(debugger)
	local stack = debugger._currentDebugSystemStack
	if #stack > 0 then stack[#stack] = nil end
end

function DebuggerHookContext.CurrentDebugSystem(debugger)
	local stack = debugger._currentDebugSystemStack
	return stack[#stack]
end

local function tagAccess(debugger, group, count, duration)
	local current = DebuggerHookContext.CurrentDebugSystem(debugger)
	if current == nil then return end
	if current ~= debugger.debugSystem then return end

	debugger._queries = debugger._queries or {}
	local samples = debugger._queryDurationSamples or {}
	debugger._queryDurationSamples = samples

	-- Source-line attribution: the call site (system code that iterated
	-- the group) is at depth 3 from this tagAccess (1=tagAccess,
	-- 2=patchedGetEntities, 3=caller). Matter uses depth 2 because their
	-- query wrapper is one frame closer; ours adds a frame.
	local file, line = debug.info(3, "sl")
	local key = (file or "?") .. ":" .. tostring(line or 0)

	local sample = samples[key]
	if sample == nil then
		sample = {}
		samples[key] = sample
	end
	RollingAverage.addSample(sample, duration)

	table.insert(debugger._queries, {
		averageDuration = RollingAverage.getAverage(sample),
		count = count,
		groupMatcher = group:matcher(),
	})
end

local function patchedGetEntities(self, buffer)
	if hookedDebugger == nil then
		return originalGetEntities(self, buffer)
	end

	local t0 = os.clock()
	local result = originalGetEntities(self, buffer)
	local dt = os.clock() - t0
	local count = result and #result or 0
	tagAccess(hookedDebugger, self, count, dt)
	return result
end

function DebuggerHookContext.Hook(debugger)
	hookedDebugger = debugger
	Group.GetEntities = patchedGetEntities
end

function DebuggerHookContext.Unhook()
	hookedDebugger = nil
	Group.GetEntities = originalGetEntities
end

return DebuggerHookContext
