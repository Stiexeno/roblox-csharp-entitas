--!strict
-- Runtime spec runner. Executes the ECS core (runtime/*.luau) inside
-- Lune — no Roblox, no transpiler — so entity/group/pool behavior is
-- tested for real instead of via source-shape assertions.
--
--   lune run tests/runtime/run        (from the plugin root)
--
-- Runtime modules use Roblox-style `require(script.Parent.X)`; the
-- loader below shims `script` so those resolve to runtime/<X>.luau.

local fs = require("@lune/fs")
local luau = require("@lune/luau")
local process = require("@lune/process")

local RUNTIME_DIR = "runtime"
local SPECS_DIR = "tests/runtime/specs"

-- ---------------------------------------------------------------------
-- Module loader with `script.Parent.X` shim
-- ---------------------------------------------------------------------

local cache: { [string]: { value: any } } = {}

local function loadModule(name: string): any
	local cached = cache[name]
	if cached then return cached.value end

	local path = `{RUNTIME_DIR}/{name}.luau`
	local src = fs.readFile(path)

	local parentProxy = setmetatable({}, {
		__index = function(_, child)
			return { __module = child }
		end,
	})

	local env = {
		script = { Parent = parentProxy, Name = name },
		require = function(target)
			assert(type(target) == "table" and target.__module ~= nil,
				`unexpected require target in {name}`)
			return loadModule(target.__module)
		end,
	}

	local fn = luau.load(src, {
		debugName = name,
		environment = env,
		injectGlobals = true,
	})
	local value = fn()
	cache[name] = { value = value }
	return value
end

-- ---------------------------------------------------------------------
-- Tiny spec harness
-- ---------------------------------------------------------------------

local passed = 0
local failed = 0

local function test(name: string, body: () -> ())
	local ok, err = pcall(body)
	if ok then
		passed += 1
	else
		failed += 1
		print(`  FAIL {name}`)
		print(`       {err}`)
	end
end

local function expectError(body: () -> (), messagePart: string?)
	local ok, err = pcall(body)
	assert(not ok, "expected an error but the call succeeded")
	if messagePart ~= nil then
		assert(string.find(tostring(err), messagePart, 1, true) ~= nil,
			`expected error containing "{messagePart}", got: {tostring(err)}`)
	end
end

-- ---------------------------------------------------------------------
-- Spec discovery
-- ---------------------------------------------------------------------

local harness = {
	load = loadModule,
	test = test,
	expectError = expectError,
}

for _, fileName in fs.readDir(SPECS_DIR) do
	if not string.match(fileName, "%.spec%.luau$") then continue end
	print(fileName)
	local src = fs.readFile(`{SPECS_DIR}/{fileName}`)
	local fn = luau.load(src, {
		debugName = fileName,
		environment = { harness = harness },
		injectGlobals = true,
	})
	fn()
end

print("")
print(`{passed} passed, {failed} failed`)
if failed > 0 then
	process.exit(1)
end
