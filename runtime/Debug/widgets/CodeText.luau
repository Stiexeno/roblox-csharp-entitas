-- Monospace text block. Used for system source paths and error text.

return function(debugger, text)
	local plasma = debugger.plasma
	local create = plasma.create

	plasma.widget(function()
		plasma.useInstance(function()
			return create("TextLabel", {
				BackgroundColor3 = Color3.fromRGB(28, 28, 32),
				BackgroundTransparency = 0.0,
				BorderSizePixel = 0,
				Size = UDim2.new(1, 0, 0, 0),
				AutomaticSize = Enum.AutomaticSize.Y,
				Font = Enum.Font.Code,
				TextSize = 13,
				TextColor3 = Color3.fromRGB(230, 230, 230),
				TextWrapped = true,
				TextXAlignment = Enum.TextXAlignment.Left,
				Text = text or "",
				[create] = {
					create("UIPadding", {
						PaddingLeft = UDim.new(0, 6),
						PaddingRight = UDim.new(0, 6),
						PaddingTop = UDim.new(0, 4),
						PaddingBottom = UDim.new(0, 4),
					}),
				},
			})
		end)
	end)
end
