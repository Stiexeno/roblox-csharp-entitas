-- System-name resolution without transpiler help. Pure Lua tables don't
-- carry their class name, so we recover it from debug.info on whichever
-- phase method the system implements. The source path Roblox hands back
-- looks like "=ReplicatedStorage.Generated.Systems.MovementSystem" or
-- "Game.ServerScriptService.Foo.MovementSystem" — split on "." and the
-- tail is the class. Memoized per instance; overrides keyed by class
-- table (set via Debugger:RegisterSystemName).

local SystemNameCache = {}

SystemNameCache._byInstance = setmetatable({}, { __mode = "k" })  -- weak: don't pin systems alive
SystemNameCache._overrides = {}                                    -- [classTable] = name

local function pathTail(source)
	if source == nil or source == "" then return nil end
	-- Roblox prefixes a script source with "=" when the script lives at a
	-- known instance path; strip it.
	if string.sub(source, 1, 1) == "=" then
		source = string.sub(source, 2)
	end
	local segments = string.split(source, ".")
	-- Some sources end with a script extension or a line marker; the last
	-- segment is the class name in typical Rojo layouts.
	local tail = segments[#segments]
	-- Strip a trailing :line suffix if Roblox ever appends one to "s" info.
	local colon = string.find(tail, ":", 1, true)
	if colon ~= nil then tail = string.sub(tail, 1, colon - 1) end
	return tail
end

function SystemNameCache.GetName(system)
	local cached = SystemNameCache._byInstance[system]
	if cached ~= nil then return cached end

	-- Check the registry by the system's class table (metatable). The
	-- transpiler pattern is `local MovementSystem = {}; MovementSystem.__index
	-- = MovementSystem` so getmetatable(instance) yields the class table.
	local mt = getmetatable(system)
	if mt ~= nil then
		local override = SystemNameCache._overrides[mt]
		if override ~= nil then
			SystemNameCache._byInstance[system] = override
			return override
		end
	end

	-- Resolve via debug.info on whichever phase method the system implements.
	local phaseFn = system.Execute or system.Initialize or system.Cleanup or system.TearDown
	local name
	if phaseFn ~= nil then
		local source = debug.info(phaseFn, "s")
		name = pathTail(source)
	end
	if name == nil or name == "" then
		name = tostring(system)
	end

	SystemNameCache._byInstance[system] = name
	return name
end

function SystemNameCache.RegisterSystemName(classTable, name)
	SystemNameCache._overrides[classTable] = name
end

-- Test seam: clear memoization (used by unit tests that swap class
-- tables between asserts).
function SystemNameCache._Clear()
	SystemNameCache._byInstance = setmetatable({}, { __mode = "k" })
end

return SystemNameCache
