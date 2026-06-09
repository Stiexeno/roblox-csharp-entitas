-- Hover-tooltip floater. Pinned to mouse position; used when a system
-- name is truncated in the list and the user hovers to see the full
-- thing.

local UserInputService = game:GetService("UserInputService")

return function(debugger, text, props)
	local plasma = debugger.plasma
	local create = plasma.create
	props = props or {}

	plasma.widget(function()
		plasma.useInstance(function()
			local mouse = UserInputService:GetMouseLocation()
			return create("Frame", {
				BackgroundColor3 = Color3.fromRGB(20, 20, 24),
				BackgroundTransparency = props.backgroundTransparency or 0.0,
				BorderSizePixel = 0,
				Position = UDim2.fromOffset(mouse.X + 12, mouse.Y + 12),
				Size = UDim2.fromOffset(0, 0),
				AutomaticSize = Enum.AutomaticSize.XY,
				ZIndex = 100,
				[create] = {
					create("UICorner", { CornerRadius = UDim.new(0, 4) }),
					create("UIPadding", {
						PaddingLeft = UDim.new(0, 6),
						PaddingRight = UDim.new(0, 6),
						PaddingTop = UDim.new(0, 4),
						PaddingBottom = UDim.new(0, 4),
					}),
					create("TextLabel", {
						BackgroundTransparency = 1,
						Size = UDim2.fromOffset(0, 0),
						AutomaticSize = Enum.AutomaticSize.XY,
						Font = Enum.Font.Gotham,
						TextSize = 12,
						TextColor3 = Color3.fromRGB(220, 220, 220),
						RichText = true,
						Text = text,
					}),
				},
			})
		end)
	end)
end
