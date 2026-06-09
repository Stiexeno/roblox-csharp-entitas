-- Left-side sidebar panel: dark-translucent column with a fixed width,
-- body drawn inside. plasma.widget wraps so the layout/lifecycle are
-- captured in the immediate-mode tree.

return function(debugger, body)
	local plasma = debugger.plasma
	local create = plasma.create

	plasma.widget(function()
		plasma.useInstance(function()
			return create("Frame", {
				BackgroundColor3 = Color3.fromRGB(20, 20, 24),
				BackgroundTransparency = 0.1,
				BorderSizePixel = 0,
				Size = UDim2.new(0, 260, 1, 0),
				[create] = {
					create("UIListLayout", {
						FillDirection = Enum.FillDirection.Vertical,
						HorizontalAlignment = Enum.HorizontalAlignment.Left,
						Padding = UDim.new(0, 6),
						SortOrder = Enum.SortOrder.LayoutOrder,
					}),
					create("UIPadding", {
						PaddingTop = UDim.new(0, 46),
						PaddingLeft = UDim.new(0, 10),
						PaddingRight = UDim.new(0, 10),
						PaddingBottom = UDim.new(0, 10),
					}),
				},
			})
		end)

		if body then body() end
	end)
end
