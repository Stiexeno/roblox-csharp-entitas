-- Feature (alpha). Systems + a name string for debugging.
-- Subclass of Systems via metatable __index.

local Systems = require(script.Parent.Systems)

local Feature = setmetatable({}, { __index = Systems })
Feature.__index = Feature

function Feature.new(name)
	local self = setmetatable({}, Feature)
	-- Inline the Systems constructor so we share its list initialisers
	-- without paying for a double new + copy.
	Systems.constructor(self)
	self.name = name or "Feature"

	-- Auto-attach the debugger profiler if Debugger.Run has been called.
	-- Read off the tiny DebuggerSingleton module instead of Debugger itself
	-- so projects that never call Debugger.Run don't drag the plasma +
	-- widgets graph in through Feature's require chain.
	local current = require(script.Parent.DebuggerSingleton).Current
	if current ~= nil then
		current:_attachFeatureProfiler(self)
	end

	return self
end

return Feature
