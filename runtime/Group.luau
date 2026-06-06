-- Entitas Group (alpha port). Holds entities matching a given matcher.
-- Context calls HandleEntitySilently on group when it knows membership
-- changed; the group recomputes via matcher:Matches.

local Group = {}
Group.__index = Group

function Group.new(matcher)
	local self = setmetatable({}, Group)
	return self:constructor(matcher) or self
end

function Group:constructor(matcher)
	self.matcher = matcher
	self._entities = {}   -- set: [entity] = true
	self._count = 0
	self._entitiesCache = nil
	self._singleEntityCache = nil
end

function Group:get_count() return self._count end

local function invalidate(self)
	self._entitiesCache = nil
	self._singleEntityCache = nil
end

function Group:HandleEntitySilently(entity)
	if self.matcher:Matches(entity) then
		if not self._entities[entity] then
			self._entities[entity] = true
			self._count = self._count + 1
			invalidate(self)
		end
	else
		if self._entities[entity] then
			self._entities[entity] = nil
			self._count = self._count - 1
			invalidate(self)
		end
	end
end

function Group:HandleEntity(entity, _index, _component)
	self:HandleEntitySilently(entity)
end

function Group:ContainsEntity(entity)
	return self._entities[entity] == true
end

function Group:GetEntities(buffer)
	if buffer ~= nil then
		-- Clear-and-fill the supplied buffer (perf-sensitive callers
		-- reuse one across frames). Matches Entitas 1.14 semantics.
		for i = #buffer, 1, -1 do buffer[i] = nil end
		for e, _ in pairs(self._entities) do
			table.insert(buffer, e)
		end
		return buffer
	end

	if self._entitiesCache == nil then
		self._entitiesCache = {}
		for e, _ in pairs(self._entities) do
			table.insert(self._entitiesCache, e)
		end
	end
	return self._entitiesCache
end

function Group:GetSingleEntity()
	if self._count ~= 1 then
		error("Group expected exactly one entity but contains " .. tostring(self._count) .. ".")
	end
	if self._singleEntityCache == nil then
		for e, _ in pairs(self._entities) do
			self._singleEntityCache = e
			break
		end
	end
	return self._singleEntityCache
end

function Group:AsEnumerable()
	-- The transpiler lowers `foreach (var e in group.AsEnumerable())` to
	-- a generic `for k, v in pairs(...) do` shape — but Lua's pairs over
	-- a set table yields `entity, true`. We hand back GetEntities()'s
	-- array form so the iterator pattern works either way.
	return self:GetEntities()
end

return Group
