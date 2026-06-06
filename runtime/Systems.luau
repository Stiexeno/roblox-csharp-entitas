-- Entitas Systems runner (alpha port). Add() buckets each system into the
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
	for i = 1, #self._initializeSystems do self._initializeSystems[i]:Initialize() end
end

function Systems:Execute()
	for i = 1, #self._executeSystems do self._executeSystems[i]:Execute() end
end

function Systems:Cleanup()
	for i = 1, #self._cleanupSystems do self._cleanupSystems[i]:Cleanup() end
end

function Systems:TearDown()
	for i = 1, #self._tearDownSystems do self._tearDownSystems[i]:TearDown() end
end

return Systems
