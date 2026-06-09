-- Root layout container. Uses plasma.widget to wrap a body draw fn,
-- applying a UIListLayout with the given direction/padding/margin.
--
-- Children in plasma.create are passed positionally (numeric keys);
-- string keys are properties. Matter splits "frame" and "container"; we
-- collapse both into one body-fn wrapper.

return function(debugger, props, body)
	props = props or {}
	local plasma = debugger.plasma
	local create = plasma.create

	plasma.scope(function()
		plasma.useInstance(function()
			return create("Frame", {
				BackgroundTransparency = 1,
				Size = UDim2.fromScale(1, 1),
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
			})
		end)

		if body then body() end
	end)
end
