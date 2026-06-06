-- Entitas Entity (alpha port). Parent of generated GameEntity / InputEntity / etc.
-- Holds components in an integer-keyed table; the codegen-emitted entity
-- extensions go through AddComponent / HasComponent / etc.
--
-- Simplified vs Entitas 1.14: no AERC, no component pools, no event delegates.
-- See stubs/Entity.cs for the matching C# surface.

local Entity = {}
Entity.__index = Entity

function Entity.new()
	local self = setmetatable({}, Entity)
	return self:constructor() or self
end

function Entity:constructor()
	self._components = {}
	self.totalComponents = 0
	self.creationIndex = 0
	self.isEnabled = false
end

function Entity:Initialize(creationIndex, totalComponents)
	self.creationIndex = creationIndex
	self.totalComponents = totalComponents
	self.isEnabled = true
end

function Entity:Reactivate(creationIndex)
	self.creationIndex = creationIndex
	self.isEnabled = true
end

function Entity:AddComponent(index, component)
	if not self.isEnabled then error("Cannot add component to a destroyed entity.") end
	if self._components[index] ~= nil then
		error("Entity already has component at index " .. tostring(index) .. ".")
	end
	self._components[index] = component
end

function Entity:RemoveComponent(index)
	if not self.isEnabled then error("Cannot remove component from a destroyed entity.") end
	if self._components[index] == nil then
		error("Entity does not have component at index " .. tostring(index) .. ".")
	end
	self._components[index] = nil
end

function Entity:ReplaceComponent(index, component)
	if not self.isEnabled then error("Cannot replace component on a destroyed entity.") end
	if component == nil then
		self._components[index] = nil
		return
	end
	self._components[index] = component
end

function Entity:GetComponent(index)
	local c = self._components[index]
	if c == nil then
		error("Entity does not have component at index " .. tostring(index) .. ".")
	end
	return c
end

function Entity:GetComponents()
	local result = {}
	for _, c in pairs(self._components) do
		table.insert(result, c)
	end
	return result
end

function Entity:GetComponentIndices()
	local result = {}
	for k, _ in pairs(self._components) do
		table.insert(result, k)
	end
	return result
end

function Entity:HasComponent(index)
	return self._components[index] ~= nil
end

function Entity:HasComponents(indices)
	for i = 1, #indices do
		if self._components[indices[i]] == nil then return false end
	end
	return true
end

function Entity:HasAnyComponent(indices)
	for i = 1, #indices do
		if self._components[indices[i]] ~= nil then return true end
	end
	return false
end

function Entity:RemoveAllComponents()
	self._components = {}
end

function Entity:Destroy()
	self:RemoveAllComponents()
	self.isEnabled = false
end

return Entity
