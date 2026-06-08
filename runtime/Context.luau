-- Context (alpha). Parent of generated GameContext / InputContext.
-- Holds the active entity set, tracks creationIndex, lazily creates groups,
-- broadcasts entity-component changes to interested groups, and maintains
-- a reuse stack so destroyed entities recycle instead of churning the GC.

local Group = require(script.Parent.Group)

local Context = {}
Context.__index = Context

function Context.new(totalComponents)
	local self = setmetatable({}, Context)
	return self:constructor(totalComponents) or self
end

function Context:constructor(totalComponents)
	self._totalComponents = totalComponents or 0
	self._entities = {}             -- set: [entity] = true
	self._count = 0
	self._reusableEntities = {}     -- stack-as-array; destroyed entities live here
	self._componentPools = {}       -- index → stack-as-array of recycled components
	self._groups = {}               -- IMatcher → Group (keyed by table identity)
	self._groupsByIndex = {}        -- componentIndex → array of groups interested in that index
	self._creationIndex = 0
	self._entitiesCache = nil
end

-- Drops every component sitting on the pool stack for a single index.
-- Useful between scenes when pooled instances might reference disposed
-- handles (Roblox Instances, Connections, etc.).
function Context:ClearComponentPool(index)
	self._componentPools[index] = nil
end

function Context:ClearComponentPools()
	self._componentPools = {}
end

-- Default no-op so direct `new Context<...>()` users get a clear error
-- instead of a nil call. Real games use a codegen-emitted subclass that
-- overrides this.
function Context:CreateEntityInstance()
	error("Context.CreateEntityInstance must be overridden by the codegen-emitted subclass.")
end

function Context:totalComponents() return self._totalComponents end
function Context:count() return self._count end
function Context:reusableEntitiesCount() return #self._reusableEntities end

function Context:CreateEntity()
	-- Reuse path: pop a destroyed husk off the stack and Reactivate.
	-- Avoids the allocator and skips the codegen-emitted CreateEntityInstance.
	if #self._reusableEntities > 0 then
		local entity = table.remove(self._reusableEntities)
		entity:Reactivate(self._creationIndex)
		self._creationIndex = self._creationIndex + 1
		self._entities[entity] = true
		self._count = self._count + 1
		self._entitiesCache = nil
		return entity
	end

	-- Fresh path: codegen-emitted subclass produces a typed entity.
	local entity = self:CreateEntityInstance()
	if entity == nil then
		error("CreateEntity() got nil — did the codegen emit CreateEntityInstance() on the partial context?")
	end
	self:Initialize(entity)
	return entity
end

-- Wires a pre-constructed entity into this context. Used by CreateEntity's
-- fresh path and by tests / framework code that wants to attach its own
-- entity instances. Sets the _context back-reference so Destroy can push
-- the entity onto _reusableEntities later.
function Context:Initialize(entity)
	entity._context = self
	entity:Initialize(self._creationIndex, self._totalComponents)
	self._creationIndex = self._creationIndex + 1
	self._entities[entity] = true
	self._count = self._count + 1
	self._entitiesCache = nil
end

-- Internal callback Entity:Destroy fires. Pulls the entity out of the
-- active set and onto the reuse stack.
function Context:_OnEntityDestroyed(entity)
	if not self._entities[entity] then return end
	self._entities[entity] = nil
	self._count = self._count - 1
	self._entitiesCache = nil
	table.insert(self._reusableEntities, entity)
end

function Context:HasEntity(entity) return self._entities[entity] == true end

function Context:GetEntities()
	if self._entitiesCache == nil then
		self._entitiesCache = {}
		for e, _ in pairs(self._entities) do
			table.insert(self._entitiesCache, e)
		end
	end
	return self._entitiesCache
end

function Context:GetGroup(matcher)
	local existing = self._groups[matcher]
	if existing ~= nil then return existing end
	local group = Group.new(matcher)
	self._groups[matcher] = group

	-- Index the group by every component its matcher touches so
	-- NotifyComponentChanged below only fans out to groups that could
	-- possibly care about that index. For a context with N groups and M
	-- mutations per tick, this cuts the worst case from O(N×M) to
	-- O(M × avg(groups-per-index)) — a 10–50× win once N grows past a
	-- handful. The matcher's GetIndices returns the union of AllOf,
	-- AnyOf, and NoneOf indices (NoneOf membership can flip on either
	-- add or remove, so every clause has to be subscribed to all of its
	-- own indices).
	local indices = matcher:GetIndices()
	if indices ~= nil then
		for i = 1, #indices do
			local idx = indices[i]
			local bucket = self._groupsByIndex[idx]
			if bucket == nil then
				bucket = {}
				self._groupsByIndex[idx] = bucket
			end
			table.insert(bucket, group)
		end
	end

	-- Seed the group with current matches so user code that calls
	-- GetGroup partway through a frame sees the right set.
	for e, _ in pairs(self._entities) do
		group:HandleEntitySilently(e)
	end
	return group
end

function Context:DestroyAllEntities()
	-- Snapshot before iterating: Destroy mutates _entities through the
	-- back-channel and Lua doesn't promise iteration stability across mutation.
	local toDestroy = {}
	for e, _ in pairs(self._entities) do
		table.insert(toDestroy, e)
	end
	for i = 1, #toDestroy do
		toDestroy[i]:Destroy()
	end
end

function Context:ResetCreationIndex() self._creationIndex = 0 end

function Context:Reset()
	self:DestroyAllEntities()
	self:ResetCreationIndex()
end

-- Called by codegen-emitted entity extension methods (Add / Replace /
-- Remove) so cached groups can re-check membership. Routes through
-- _groupsByIndex so we only touch groups whose matcher includes this
-- index — see GetGroup for the registration shape.
function Context:NotifyComponentChanged(entity, index, component)
	local interested = self._groupsByIndex[index]
	if interested == nil then return end
	for i = 1, #interested do
		interested[i]:HandleEntity(entity, index, component)
	end
end

return Context
