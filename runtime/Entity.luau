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

-- Replicated multi-field getter. Hands back a frozen shallow clone so
-- user code can't bypass Replace{X} by mutating the live component
-- table in place (`e.Position.X = 5`). The pool stays unfrozen — only
-- the snapshot we hand out is frozen, so the next Replace{X} recycles
-- a clean instance normally. Caller pays one table.clone per Get, but
-- only for replicated components routed through here by codegen.
function Entity:GetReplicatedComponent(index)
	local c = self._components[index]
	if c == nil then
		error("Entity does not have component at index " .. tostring(index) .. ".")
	end
	return table.freeze(table.clone(c))
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
	-- Entitas parity (Entity.InternalDestroy → RemoveAllComponents fires
	-- HandleComponent(i, nil) per component): every removal must flow
	-- through the same notification path as RemoveComponent, so cached
	-- groups drop the entity NOW. Without this a destroyed entity stays
	-- in its groups until some unrelated mutation re-checks it — and
	-- because destroyed husks are pooled and reused, the recycled entity
	-- would still be a member of its previous life's groups.
	local indices = {}
	for index in pairs(self._components) do
		table.insert(indices, index)
	end
	table.sort(indices) -- deterministic removal order, like Entitas' ascending scan

	for i = 1, #indices do
		local index = indices[i]
		local component = self._components[index]
		self._components[index] = nil
		self:_PushToPool(index, component)
		self:_NotifyContext(index, nil)
	end
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
	-- Entitas parity: destroying twice is a bug in caller code (a stale
	-- reference to a pooled husk) — fail loud instead of silently
	-- re-pooling.
	if not self._isEnabled then
		error("Cannot destroy an already-destroyed entity.")
	end

	-- Entitas order (InternalDestroy): disable first, then strip
	-- components through the notifying path, so anything observing the
	-- removal notifications sees an entity that is already dead.
	self._isEnabled = false
	self:RemoveAllComponents()

	-- Hand the husk back to the context for reuse. The context
	-- removes us from its active set and pushes us onto its
	-- _reusableEntities stack; the next CreateEntity pops + Reactivates.
	if self._context ~= nil then
		self._context:_OnEntityDestroyed(self)
	end
end

return Entity
