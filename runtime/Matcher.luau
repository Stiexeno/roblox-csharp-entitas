-- Entitas Matcher (alpha port). Generic erased to a single concrete table.
-- Frozen-feast pattern: `GameMatcher.AllOf(GameMatcher.A).NoneOf(GameMatcher.B)`.
-- The chain mutates and returns self after each step.
--
-- The non-generic static surface (Matcher.AllOf, AllOfIndices, AnyOf)
-- takes a leading `T` argument per the converter's generic-erasure
-- convention (commit 3613bf0 in the main repo) — every generic method
-- receives its type parameter as the first runtime argument so the
-- two ends line up. We ignore T here; Luau has nothing to do with it.

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
		-- IMatcher.indices is a computed property in C# — the transpiler
		-- lowers reads to a method call. Fall back to the raw allOfIndices
		-- field if the matcher doesn't expose the getter (own runtime objects).
		local mIndices = m.allOfIndices
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
	self.allOfIndices = nil
	self.anyOfIndices = nil
	self.noneOfIndices = nil
	self.componentNames = nil
	self._indices = nil
end

function Matcher:GetIndices()
	if self._indices ~= nil then return self._indices end
	local merged = {}
	if self.allOfIndices then for i = 1, #self.allOfIndices do table.insert(merged, self.allOfIndices[i]) end end
	if self.anyOfIndices then for i = 1, #self.anyOfIndices do table.insert(merged, self.anyOfIndices[i]) end end
	if self.noneOfIndices then for i = 1, #self.noneOfIndices do table.insert(merged, self.noneOfIndices[i]) end end
	self._indices = distinctSorted(merged)
	return self._indices
end

-- C# stub property `indices` resolves through this in the transpiler.
function Matcher:indices()
	return self:GetIndices()
end

-- Static surface called by the codegen-emitted GameMatcher.AllOf etc.
-- The leading `_T` is the converter's erased type arg — ignored here.
function Matcher.AllOf(_T, matchers)
	local m = Matcher.new()
	m.allOfIndices = distinctSorted(mergeIndicesFromMatchers(matchers))
	return m
end

function Matcher.AllOfIndices(_T, indices)
	local m = Matcher.new()
	m.allOfIndices = distinctSorted(indices)
	return m
end

function Matcher.AnyOf(_T, matchers)
	local m = Matcher.new()
	m.anyOfIndices = distinctSorted(mergeIndicesFromMatchers(matchers))
	return m
end

-- Instance-chain methods. The C# stub has overloads taking either
-- `int[]` or `IMatcher<T>[]`. Lua collapses overloads — the matchers
-- form is what frozen-feast uses, so that's what we implement.
function Matcher:AnyOf(matchers)
	self.anyOfIndices = distinctSorted(mergeIndicesFromMatchers(matchers))
	self._indices = nil
	return self
end

function Matcher:NoneOf(matchers)
	self.noneOfIndices = distinctSorted(mergeIndicesFromMatchers(matchers))
	self._indices = nil
	return self
end

function Matcher:Matches(entity)
	local matchesAllOf = (self.allOfIndices == nil) or entity:HasComponents(self.allOfIndices)
	local matchesAnyOf = (self.anyOfIndices == nil) or entity:HasAnyComponent(self.anyOfIndices)
	local matchesNoneOf = (self.noneOfIndices == nil) or (not entity:HasAnyComponent(self.noneOfIndices))
	return matchesAllOf and matchesAnyOf and matchesNoneOf
end

return Matcher
