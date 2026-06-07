-- Entitas Matcher (alpha port). Generic erased to a single concrete table.
-- Frozen-feast pattern: `GameMatcher.AllOf(GameMatcher.A).NoneOf(GameMatcher.B)`.
-- The chain mutates and returns self after each step.
--
-- The non-generic static surface (Matcher.AllOf, AllOfIndices, AnyOf)
-- takes a leading `T` argument per the converter's generic-erasure
-- convention (commit 3613bf0 in the main repo) — every generic method
-- receives its type parameter as the first runtime argument so the
-- two ends line up. We ignore T here; Luau has nothing to do with it.
--
-- Every C# stub property is exposed as a method here (alpha.48
-- convention). Backing fields use `_<name>` so the bare-name accessor
-- isn't shadowed by an instance field.

local Matcher = {}
Matcher.__index = Matcher

local function distinctSorted(indices)
	if indices == nil then return nil end
	local seen = {}
	local result = {}
	for i = 1, #indices do
		local v = indices[i]
		if not seen[v] then
			seen[v] = true
			table.insert(result, v)
		end
	end
	table.sort(result)
	return result
end

local function mergeIndicesFromMatchers(matchers)
	local merged = {}
	for i = 1, #matchers do
		local m = matchers[i]
		-- Prefer the public `indices()` accessor (works for any matcher,
		-- including foreign runtimes). Falls back to the raw _allOfIndices
		-- field for own matchers that haven't been chained yet.
		local mIndices = nil
		if type(m.indices) == "function" then mIndices = m:indices() end
		if mIndices == nil then mIndices = m._allOfIndices end
		if mIndices == nil and type(m.GetIndices) == "function" then
			mIndices = m:GetIndices()
		end
		if mIndices then
			for j = 1, #mIndices do
				table.insert(merged, mIndices[j])
			end
		end
	end
	return merged
end

function Matcher.new()
	local self = setmetatable({}, Matcher)
	return self:constructor() or self
end

function Matcher:constructor()
	self._allOfIndices = nil
	self._anyOfIndices = nil
	self._noneOfIndices = nil
	self._componentNames = nil
	self._indices = nil
end

function Matcher:GetIndices()
	if self._indices ~= nil then return self._indices end
	local merged = {}
	if self._allOfIndices then for i = 1, #self._allOfIndices do table.insert(merged, self._allOfIndices[i]) end end
	if self._anyOfIndices then for i = 1, #self._anyOfIndices do table.insert(merged, self._anyOfIndices[i]) end end
	if self._noneOfIndices then for i = 1, #self._noneOfIndices do table.insert(merged, self._noneOfIndices[i]) end end
	self._indices = distinctSorted(merged)
	return self._indices
end

-- C# stub properties — every extern property is method-backed so the
-- transpiler's `:name()` dispatch (alpha.48) lands on the accessor.
function Matcher:indices() return self:GetIndices() end
function Matcher:allOfIndices() return self._allOfIndices end
function Matcher:anyOfIndices() return self._anyOfIndices end
function Matcher:noneOfIndices() return self._noneOfIndices end
function Matcher:componentNames() return self._componentNames end
function Matcher:set_componentNames(value) self._componentNames = value end

-- Static surface called by the codegen-emitted GameMatcher.AllOf etc.
-- The leading `_T` is the converter's erased type arg — ignored here.
function Matcher.AllOf(_T, matchers)
	local m = Matcher.new()
	m._allOfIndices = distinctSorted(mergeIndicesFromMatchers(matchers))
	return m
end

function Matcher.AllOfIndices(_T, indices)
	local m = Matcher.new()
	m._allOfIndices = distinctSorted(indices)
	return m
end

function Matcher.AnyOf(_T, matchers)
	local m = Matcher.new()
	m._anyOfIndices = distinctSorted(mergeIndicesFromMatchers(matchers))
	return m
end

-- Instance-chain methods. The C# stub has overloads taking either
-- `int[]` or `IMatcher<T>[]`. Lua collapses overloads — the matchers
-- form is what frozen-feast uses, so that's what we implement.
function Matcher:AnyOf(matchers)
	self._anyOfIndices = distinctSorted(mergeIndicesFromMatchers(matchers))
	self._indices = nil
	return self
end

function Matcher:NoneOf(matchers)
	self._noneOfIndices = distinctSorted(mergeIndicesFromMatchers(matchers))
	self._indices = nil
	return self
end

function Matcher:Matches(entity)
	local matchesAllOf = (self._allOfIndices == nil) or entity:HasComponents(self._allOfIndices)
	local matchesAnyOf = (self._anyOfIndices == nil) or entity:HasAnyComponent(self._anyOfIndices)
	local matchesNoneOf = (self._noneOfIndices == nil) or (not entity:HasAnyComponent(self._noneOfIndices))
	return matchesAllOf and matchesAnyOf and matchesNoneOf
end

return Matcher
