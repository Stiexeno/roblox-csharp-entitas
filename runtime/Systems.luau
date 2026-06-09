-- Systems runner (alpha). Add() buckets each system into the
-- phase lists it implements (cast in C# / duck-typed here). Initialize /
-- Execute / Cleanup / TearDown walk their bucket in insertion order.

local Systems = {}
Systems.__index = Systems

function Systems.new()
	local self = setmetatable({}, Systems)
	return self:constructor() or self
end

function Systems:constructor()
	self._initializeSystems = {}
	self._executeSystems = {}
	self._cleanupSystems = {}
	self._tearDownSystems = {}
	-- Optional debugger hook. When set, each per-system phase call goes
	-- through profiler(system, phaseName) instead of the direct dispatch.
	-- The profiler closes over its own timing + skip logic; we just hand it
	-- the system + the phase to invoke. Nil-checked once per phase loop so
	-- the no-debugger path branches once, not per system.
	self._profiler = nil
end

function Systems:SetProfiler(fn)
	self._profiler = fn
end

function Systems:Add(system)
	-- Duck-type: any method present qualifies the system for that phase.
	-- Matches IInitializeSystem / IExecuteSystem / ICleanupSystem /
	-- ITearDownSystem in C#.
	if type(system.Initialize) == "function" then table.insert(self._initializeSystems, system) end
	if type(system.Execute) == "function" then table.insert(self._executeSystems, system) end
	if type(system.Cleanup) == "function" then table.insert(self._cleanupSystems, system) end
	if type(system.TearDown) == "function" then table.insert(self._tearDownSystems, system) end
	return self
end

function Systems:Initialize()
	local profiler = self._profiler
	local list = self._initializeSystems
	if profiler == nil then
		for i = 1, #list do list[i]:Initialize() end
	else
		for i = 1, #list do profiler(list[i], "Initialize") end
	end
end

function Systems:Execute()
	local profiler = self._profiler
	local list = self._executeSystems
	if profiler == nil then
		for i = 1, #list do list[i]:Execute() end
	else
		for i = 1, #list do profiler(list[i], "Execute") end
	end
end

function Systems:Cleanup()
	local profiler = self._profiler
	local list = self._cleanupSystems
	if profiler == nil then
		for i = 1, #list do list[i]:Cleanup() end
	else
		for i = 1, #list do profiler(list[i], "Cleanup") end
	end
end

function Systems:TearDown()
	local profiler = self._profiler
	local list = self._tearDownSystems
	if profiler == nil then
		for i = 1, #list do list[i]:TearDown() end
	else
		for i = 1, #list do profiler(list[i], "TearDown") end
	end
end

return Systems
