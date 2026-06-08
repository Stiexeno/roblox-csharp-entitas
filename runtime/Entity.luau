-- Entity (alpha). Parent of generated GameEntity / InputEntity / etc.
-- Holds components in an integer-keyed table; the codegen-emitted entity
-- extensions go through AddComponent / HasComponent / etc.
--
-- _context back-reference is set by Context:Initialize so Destroy can push
-- this entity onto the context's reuse stack, and so Remove/Replace can push
-- displaced components onto the context's per-index componentPools stacks.

local Entity = {}
Entity.__index = Entity

function Entity.new()
	local self = setmetatable({}, Entity)
	return self:constructor() or self
end

function Entity:constructor()
	self._components = {}
	self._context = nil
	self._totalComponents = 0
	self._creationIndex = 0
	self._isEnabled = false
end

function Entity:totalComponents() return self._totalComponents end
function Entity:creationIndex() return self._creationIndex end
function Entity:isEnabled() return self._isEnabled end
function Entity:context() return self._context end

function Entity:Initialize(creationIndex, totalComponents)
	self._creationIndex = creationIndex
	self._totalComponents = totalComponents
	self._isEnabled = true
end

function Entity:Reactivate(creationIndex)
	self._creationIndex = creationIndex
	self._isEnabled = true
end

-- Pushes a displaced component onto the context's pool stack for that
-- index. No-op if there's no context (orphaned entity, test scaffolding).
function Entity:_PushToPool(index, component)
	if self._context == nil then return end
	local pools = self._context._componentPools
	if pools[index] == nil then pools[index] = {} end
	table.insert(pools[index], component)
end

-- Notifies the context that this entity's component at `index` changed.
-- Context fans out to every cached group, which re-checks matcher membership.
function Entity:_NotifyContext(index, component)
	if self._context ~= nil then
		self._context:NotifyComponentChanged(self, index, component)
	end
end

function Entity:AddComponent(index, component)
	if not self._isEnabled then error("Cannot add component to a destroyed entity.") end
	if self._components[index] ~= nil then
		error("Entity already has component at index " .. tostring(index) .. ".")
	end
	self._components[index] = component
	self:_NotifyContext(index, component)
end

function Entity:RemoveComponent(index)
	if not self._isEnabled then error("Cannot remove component from a destroyed entity.") end
	local existing = self._components[index]
	if existing == nil then
		error("Entity does not have component at index " .. tostring(index) .. ".")
	end
	self._components[index] = nil
	self:_PushToPool(index, existing)
	self:_NotifyContext(index, nil)
end

function Entity:ReplaceComponent(index, component)
	if not self._isEnabled then error("Cannot replace component on a destroyed entity.") end
	local existing = self._components[index]
	if component == nil then
		if existing ~= nil then
			self._components[index] = nil
			self:_PushToPool(index, existing)
			self:_NotifyContext(index, nil)
		end
		return
	end
	if existing ~= nil then
		self:_PushToPool(index, existing)
	end
	self._components[index] = component
	self:_NotifyContext(index, component)
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
	-- Push every live component to the pool, then clear.
	for index, component in pairs(self._components) do
		self:_PushToPool(index, component)
	end
	self._components = {}
end

-- Pool-aware allocator. The codegen-emitted AddX/ReplaceX call this with
-- the component's class table as T (generic-erased per the converter's
-- convention) plus the lookup index. Pops a recycled instance off the
-- context's componentPools[index] stack if available, otherwise news up.
function Entity:CreateComponent(T, index)
	if self._context ~= nil then
		local pool = self._context._componentPools[index]
		if pool and #pool > 0 then
			return table.remove(pool)
		end
	end
	return T.new()
end

function Entity:Destroy()
	self:RemoveAllComponents()
	self._isEnabled = false
	-- Hand the husk back to the context for reuse. The context
	-- removes us from its active set and pushes us onto its
	-- _reusableEntities stack; the next CreateEntity pops + Reactivates.
	if self._context ~= nil then
		self._context:_OnEntityDestroyed(self)
	end
end

return Entity
