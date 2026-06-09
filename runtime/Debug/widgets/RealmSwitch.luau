-- Top-left button toggling client view ↔ server view. Mirrors Matter's
-- realmSwitch: shows the "other side" as the action.

local RunService = game:GetService("RunService")

local IS_SERVER = RunService:IsServer()

return function(debugger)
	local plasma = debugger.plasma
	local label
	if IS_SERVER then
		label = "← client"  -- on server-rendered UI, click to send client back
	elseif debugger:IsServerView() then
		label = "← client"
	else
		label = "server →"
	end

	local handle = plasma.button(label)
	return {
		clicked = function() return handle:clicked() end,
	}
end
