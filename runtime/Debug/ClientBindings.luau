-- Client-side OnClientEvent dispatcher. Singleton — Connect(debugger) is
-- idempotent across multiple Debugger instances (uncommon, but defensive).
-- When the server fires ("connect" | "disconnect" | …) the client looks up
-- the action in DebuggerEventBridge.clientActions and runs it, passing a
-- closure that forwards back to the server.

local DebuggerEventBridge = require(script.Parent.DebuggerEventBridge)

local ClientBindings = {}

local connected = false

function ClientBindings.Connect(debugger)
	if connected then return end
	connected = true

	local remote = debugger._wire
	remote.OnClientEvent:Connect(function(action, ...)
		local handler = DebuggerEventBridge.clientActions[action]
		if handler == nil then return end
		handler(function(...) remote:FireServer(...) end, ...)
	end)
end

return ClientBindings
