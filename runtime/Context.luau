-- Entitas Context (alpha port). Parent of generated GameContext / InputContext.
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
	self.totalComponents = totalComponents or 0
	self._entities = {}             -- set: [entity] = true
	self._count = 0
	self._reusableEntities = {}     -- stack-as-array; destroyed entities live here
	self._groups = {}               -- IMatcher → Group (keyed by table identity)
	self._creationIndex = 0
	self._entitiesCache = nil
end

-- Default no-op so direct `new Context<...>()` users get a clear error
-- instead of a nil call. Real games use a codegen-emitted subclass that
-- overrides this.
function Context:CreateEntityInstance()
	error("Context.CreateEntityInstance must be overridden by the codegen-emitted subclass.")
end

function Context:get_count() return self._count end
function Context:get_reusableEntitiesCount() return #self._reusableEntities end

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
	entity:Initialize(self._creationIndex, self.totalComponents)
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
-- Remove) so cached groups can re-check membership.
function Context:NotifyComponentChanged(entity, index, component)
	for _, group in pairs(self._groups) do
		group:HandleEntity(entity, index, component)
	end
end

return Context
