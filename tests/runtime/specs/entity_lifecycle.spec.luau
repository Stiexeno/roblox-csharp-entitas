-- Entity lifecycle: create, destroy, pool reuse, stale-reference guards.
local test = harness.test
local expectError = harness.expectError
local Entity = harness.load("Entity")
local Context = harness.load("Context")

local HEALTH = 1
local SPEED = 2

local function newContext()
	return Context.new(8)
end

local function spawn(ctx)
	local e = Entity.new()
	ctx:Initialize(e)
	return e
end

test("create wires context, enables, assigns ascending creationIndex", function()
	local ctx = newContext()
	local a = spawn(ctx)
	local b = spawn(ctx)
	assert(a:isEnabled(), "entity should be enabled after Initialize")
	assert(a:context() == ctx, "entity should back-reference its context")
	assert(b:creationIndex() > a:creationIndex(), "creationIndex should ascend")
	assert(ctx:count() == 2, "context should count both entities")
end)

test("destroy removes from context and pools the husk", function()
	local ctx = newContext()
	local e = spawn(ctx)
	e:AddComponent(HEALTH, { value = 10 })
	e:Destroy()
	assert(ctx:count() == 0, "context should be empty after destroy")
	assert(ctx:reusableEntitiesCount() == 1, "husk should be pooled")
	assert(not e:isEnabled(), "destroyed entity should be disabled")
end)

test("destroyed husk is reused with a fresh creationIndex", function()
	local ctx = newContext()
	local a = spawn(ctx)
	local firstIndex = a:creationIndex()
	a:Destroy()
	local b = ctx:CreateEntity()
	assert(b == a, "CreateEntity should pop the pooled husk")
	assert(b:isEnabled(), "reused entity should be enabled")
	assert(b:creationIndex() > firstIndex, "reused entity must get a fresh creationIndex")
	assert(not b:HasComponent(HEALTH), "reused entity must start with no components")
end)

test("double destroy errors", function()
	local ctx = newContext()
	local e = spawn(ctx)
	e:Destroy()
	expectError(function() e:Destroy() end, "already-destroyed")
end)

test("reading a destroyed husk errors with stale-reference message", function()
	local ctx = newContext()
	local e = spawn(ctx)
	e:AddComponent(HEALTH, { value = 10 })
	e:Destroy()
	expectError(function() e:GetComponent(HEALTH) end, "stale reference")
end)

test("mutating a destroyed husk errors", function()
	local ctx = newContext()
	local e = spawn(ctx)
	e:Destroy()
	expectError(function() e:AddComponent(SPEED, {}) end, "destroyed")
end)

test("add duplicate component errors; get missing errors", function()
	local ctx = newContext()
	local e = spawn(ctx)
	e:AddComponent(HEALTH, { value = 1 })
	expectError(function() e:AddComponent(HEALTH, { value = 2 }) end, "already has component")
	expectError(function() e:GetComponent(SPEED) end, "does not have component")
end)

test("removed component instance is recycled by the pool", function()
	local ctx = newContext()
	local e = spawn(ctx)
	local component = { value = 42 }
	e:AddComponent(HEALTH, component)
	e:RemoveComponent(HEALTH)
	local recycled = e:CreateComponent({ new = function() return {} end }, HEALTH)
	assert(recycled == component, "CreateComponent should pop the pooled instance")
end)

test("DestroyAllEntities empties the context", function()
	local ctx = newContext()
	spawn(ctx)
	spawn(ctx)
	spawn(ctx)
	ctx:DestroyAllEntities()
	assert(ctx:count() == 0, "context should be empty")
	assert(ctx:reusableEntitiesCount() == 3, "all husks should be pooled")
end)
