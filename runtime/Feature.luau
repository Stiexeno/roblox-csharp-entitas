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
	return self
end

return Feature
