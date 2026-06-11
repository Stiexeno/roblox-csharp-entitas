-- Group membership through the full mutation surface — including the
-- destroy/reuse path that regressed before these specs existed.
local test = harness.test
local Entity = harness.load("Entity")
local Context = harness.load("Context")
local Matcher = harness.load("Matcher")

local HEALTH = 1
local SPEED = 2
local DEAD = 3

local function newContext()
	return Context.new(8)
end

local function spawn(ctx)
	local e = Entity.new()
	ctx:Initialize(e)
	return e
end

local function allOf(...)
	return Matcher.AllOfIndices(nil, { ... })
end

test("add component joins matching group", function()
	local ctx = newContext()
	local group = ctx:GetGroup(allOf(HEALTH))
	local e = spawn(ctx)
	e:AddComponent(HEALTH, { value = 1 })
	assert(group:ContainsEntity(e), "entity should join group on add")
	assert(group:count() == 1)
end)

test("remove component leaves group", function()
	local ctx = newContext()
	local group = ctx:GetGroup(allOf(HEALTH))
	local e = spawn(ctx)
	e:AddComponent(HEALTH, { value = 1 })
	e:RemoveComponent(HEALTH)
	assert(not group:ContainsEntity(e), "entity should leave group on remove")
	assert(group:count() == 0)
end)

test("replace keeps membership and swaps the component", function()
	local ctx = newContext()
	local group = ctx:GetGroup(allOf(HEALTH))
	local e = spawn(ctx)
	e:AddComponent(HEALTH, { value = 1 })
	e:ReplaceComponent(HEALTH, { value = 2 })
	assert(group:ContainsEntity(e), "replace should not change membership")
	assert(e:GetComponent(HEALTH).value == 2)
end)

test("destroy removes the entity from every group", function()
	local ctx = newContext()
	local healthGroup = ctx:GetGroup(allOf(HEALTH))
	local speedGroup = ctx:GetGroup(allOf(SPEED))
	local e = spawn(ctx)
	e:AddComponent(HEALTH, { value = 1 })
	e:AddComponent(SPEED, { value = 5 })
	e:Destroy()
	assert(not healthGroup:ContainsEntity(e), "destroyed entity must leave health group")
	assert(not speedGroup:ContainsEntity(e), "destroyed entity must leave speed group")
	assert(healthGroup:count() == 0 and speedGroup:count() == 0)
end)

test("reused husk does not inherit previous life's groups", function()
	local ctx = newContext()
	local healthGroup = ctx:GetGroup(allOf(HEALTH))
	local e = spawn(ctx)
	e:AddComponent(HEALTH, { value = 1 })
	e:Destroy()

	local reused = ctx:CreateEntity()
	assert(reused == e, "precondition: husk is reused")
	reused:AddComponent(SPEED, { value = 5 })
	assert(not healthGroup:ContainsEntity(reused),
		"recycled entity must not appear in groups from its previous life")
end)

test("GetGroup after entities exist seeds current matches", function()
	local ctx = newContext()
	local e = spawn(ctx)
	e:AddComponent(HEALTH, { value = 1 })
	local group = ctx:GetGroup(allOf(HEALTH))
	assert(group:ContainsEntity(e), "late-created group should seed existing matches")
end)

test("NoneOf membership flips on add and remove", function()
	local ctx = newContext()
	local alive = ctx:GetGroup(allOf(HEALTH):NoneOf({ allOf(DEAD) }))
	local e = spawn(ctx)
	e:AddComponent(HEALTH, { value = 1 })
	assert(alive:ContainsEntity(e), "matches AllOf with no NoneOf hit")
	e:AddComponent(DEAD, {})
	assert(not alive:ContainsEntity(e), "NoneOf component must eject the entity")
	e:RemoveComponent(DEAD)
	assert(alive:ContainsEntity(e), "removing the NoneOf component re-admits")
end)

test("destroying entities while iterating a snapshot is safe", function()
	local ctx = newContext()
	local group = ctx:GetGroup(allOf(HEALTH))
	for _ = 1, 5 do
		local e = spawn(ctx)
		e:AddComponent(HEALTH, { value = 1 })
	end

	local seen = 0
	for _, e in group:GetEntities() do
		seen += 1
		e:Destroy()
	end
	assert(seen == 5, "snapshot iteration should visit all five entities")
	assert(group:count() == 0, "group should be empty after destroying all")
end)

test("DestroyAllEntities empties groups", function()
	local ctx = newContext()
	local group = ctx:GetGroup(allOf(HEALTH))
	for _ = 1, 3 do
		local e = spawn(ctx)
		e:AddComponent(HEALTH, { value = 1 })
	end
	ctx:DestroyAllEntities()
	assert(group:count() == 0, "groups must be empty after DestroyAllEntities")
end)
