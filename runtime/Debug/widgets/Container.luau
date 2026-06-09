-- Root layout container. Uses plasma.widget to wrap a body draw fn,
-- applying a UIListLayout with the given direction/padding/margin.
--
-- Matter's container splits "frame" (a plain layout box) and "container"
-- (a body-fn wrapper). We collapse both into one — the body-fn variant
-- covers everything we need.

local create = nil  -- lazy-required; injected via debugger.plasma.create

return function(debugger, props, body)
	props = props or {}
	local plasma = debugger.plasma
	create = create or plasma.create

	plasma.widget(function()
		local instance = plasma.useInstance(function()
			local frame = create("Frame", {
				BackgroundTransparency = 1,
				Size = UDim2.fromScale(1, 1),
				[create] = {
					create("UIListLayout", {
						FillDirection = props.direction or Enum.FillDirection.Vertical,
						HorizontalAlignment = props.horizontalAlignment or Enum.HorizontalAlignment.Left,
						VerticalAlignment = props.verticalAlignment or Enum.VerticalAlignment.Top,
						Padding = UDim.new(0, props.padding or 0),
						SortOrder = Enum.SortOrder.LayoutOrder,
					}),
					create("UIPadding", {
						PaddingLeft = UDim.new(0, props.marginLeft or 0),
						PaddingRight = UDim.new(0, props.marginRight or 0),
						PaddingTop = UDim.new(0, props.marginTop or 0),
						PaddingBottom = UDim.new(0, props.marginBottom or 0),
					}),
				},
			})
			return frame
		end)

		if body then body() end

		return instance
	end)
end
