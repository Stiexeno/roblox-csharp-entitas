-- Single RemoteEvent at ReplicatedStorage.Plugins.Entities.Debugger,
-- carrying the server-view bridge messages:
--   ("start")                       client → server: open server debugger
--   ("stop")                        client → server: close server debugger
--   ("inspect", entityId)           client → server: select an entity in server view
--   ("hover", entityId|nil)         client → server: mouse-hover from server view
--   ("event", instance, evName, …)  client → server: plasma input forwarded
--   ("connect", instance, evName)   server → client: please listen to evName on instance
--   ("disconnect", instance, evName) server → client: stop listening
--
-- The folder shape mirrors EntitiesReplication: it expects
-- ReplicatedStorage.Plugins.Entities to already exist (the install tool
-- creates it). The RemoteEvent itself is server-created at Debugger.new
-- time and client-awaited via WaitForChild.

local RunService = game:GetService("RunService")
local ReplicatedStorage = game:GetService("ReplicatedStorage")

local DebuggerWire = {}

-- Named DebuggerRemote (not just "Debugger") because the runtime ships
-- a sibling ModuleScript named Debugger at Plugins.Entities.Debugger —
-- WaitForChild("Debugger") would resolve to the module, not the event.
local REMOTE_NAME = "DebuggerRemote"

local function pluginsEntitiesFolder()
	local plugins = ReplicatedStorage:WaitForChild("Plugins")
	return plugins:WaitForChild("Entities")
end

function DebuggerWire.GetOrCreateRemoteEvent()
	local parent = pluginsEntitiesFolder()
	if RunService:IsServer() then
		local ev = parent:FindFirstChild(REMOTE_NAME)
		if ev == nil then
			ev = Instance.new("RemoteEvent")
			ev.Name = REMOTE_NAME
			ev.Parent = parent
		end
		return ev
	else
		-- 2-second timeout. If the server doesn't construct a Debugger
		-- (client-only debug), this returns nil and server-view paths
		-- gracefully no-op rather than the client hanging.
		return parent:WaitForChild(REMOTE_NAME, 2)
	end
end

return DebuggerWire
