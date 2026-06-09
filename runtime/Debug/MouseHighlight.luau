-- Alt-key + raycast to find an entity to inspect. The user code must tag
-- instances with `debug_serverEntityId` / `debug_clientEntityId`
-- attributes for this to find anything — Debugger reads, the game's view
-- layer writes. (Documented in plugin README.)
--
-- Holding LeftAlt over a tagged instance highlights it. Clicking opens
-- it in EntityInspect. In server view, "click to inspect" forwards via
-- the RemoteEvent so the server-side debugger selects the right entity.

local RunService = game:GetService("RunService")
local UserInputService = game:GetService("UserInputService")

local defaultRaycastParams = RaycastParams.new()
defaultRaycastParams.IgnoreWater = true

local function getInstanceOnMouse(params)
	local camera = workspace.CurrentCamera
	if camera == nil then return nil end
	local mouseLocation = UserInputService:GetMouseLocation()
	local ray = camera:ViewportPointToRay(mouseLocation.X, mouseLocation.Y)
	local result = workspace:Raycast(ray.Origin, ray.Direction * 1000, params)
	return result and result.Instance or nil
end

return function(debugger, remoteEvent)
	if not RunService:IsClient() then
		error("MouseHighlight may only run on the client")
	end

	local lastSent, setLastSent = debugger.plasma.useState()

	if UserInputService:IsKeyDown(Enum.KeyCode.LeftAlt) then
		local instance = getInstanceOnMouse(debugger.raycastParams or defaultRaycastParams)

		if instance ~= nil then
			local id
			-- Walk up the ancestry — tags typically live on a Model root,
			-- not the specific Part that the ray hit.
			while instance ~= nil and instance.Parent ~= nil do
				local attrName = debugger:IsServerView() and "debug_serverEntityId" or "debug_clientEntityId"
				id = instance:GetAttribute(attrName)
				if id ~= nil then break end
				instance = instance.Parent
			end

			if id ~= nil then
				if UserInputService:IsMouseButtonPressed(Enum.UserInputType.MouseButton1) then
					if debugger:IsServerView() then
						remoteEvent:FireServer("inspect", id)
					else
						debugger.debugEntity = id
					end
				else
					debugger.plasma.highlight(instance, {
						fillColor = Color3.fromRGB(218, 62, 62),
					})
					if debugger:IsServerView() then
						if lastSent ~= id then
							setLastSent(id)
							remoteEvent:FireServer("hover", id)
						end
					else
						debugger.hoverEntity = id
					end
				end
				return
			end
		end
	end

	if debugger:IsServerView() then
		if lastSent ~= nil then
			remoteEvent:FireServer("hover", nil)
			setLastSent(nil)
		end
	else
		debugger.hoverEntity = nil
	end
end
