-- Matcher logic: AllOf / AnyOf / NoneOf combinations against bare entities.
local test = harness.test
local Entity = harness.load("Entity")
local Matcher = harness.load("Matcher")

local A, B, C = 1, 2, 3

local function entityWith(...)
	local e = Entity.new()
	e:Initialize(0, 8)
	for _, index in { ... } do
		e:AddComponent(index, {})
	end
	return e
end

local function allOf(...)
	return Matcher.AllOfIndices(nil, { ... })
end

test("AllOf requires every index", function()
	local m = allOf(A, B)
	assert(m:Matches(entityWith(A, B)))
	assert(not m:Matches(entityWith(A)))
	assert(not m:Matches(entityWith()))
end)

test("AnyOf requires at least one index", function()
	local m = Matcher.AnyOf(nil, { allOf(A), allOf(B) })
	assert(m:Matches(entityWith(A)))
	assert(m:Matches(entityWith(B)))
	assert(not m:Matches(entityWith(C)))
end)

test("AllOf + NoneOf excludes", function()
	local m = allOf(A):NoneOf({ allOf(C) })
	assert(m:Matches(entityWith(A)))
	assert(not m:Matches(entityWith(A, C)))
end)

test("GetIndices unions all clauses, distinct and sorted", function()
	local m = allOf(B, A):NoneOf({ allOf(C) }):AnyOf({ allOf(A) })
	local indices = m:GetIndices()
	assert(#indices == 3, "expected 3 distinct indices, got " .. #indices)
	assert(indices[1] == A and indices[2] == B and indices[3] == C, "indices should be sorted")
end)
