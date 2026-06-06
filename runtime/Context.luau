-- Entitas Context (alpha port). Parent of generated GameContext / InputContext.
-- Holds the set of live entities, tracks creationIndex, lazily creates groups,
-- broadcasts entity-component changes to interested groups.

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

function Context:CreateEntity()
	-- CreateEntityInstance is overridden on the codegen-emitted subclass
	-- (GameContext, InputContext, ...) so we get a concrete typed entity.
	local entity = self:CreateEntityInstance()
	if entity == nil then
		error("CreateEntity() got nil — did the codegen emit CreateEntityInstance() on the partial context?")
	end
	entity:Initialize(self._creationIndex, self.totalComponents)
	self._creationIndex = self._creationIndex + 1
	self._entities[entity] = true
	self._count = self._count + 1
	self._entitiesCache = nil
	-- Newly created entities have no components yet, so no group will
	-- match — defer membership computation until the first Add fires
	-- NotifyComponentChanged.
	return entity
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
	for e, _ in pairs(self._entities) do
		e:Destroy()
	end
	self._entities = {}
	self._count = 0
	self._entitiesCache = nil
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
