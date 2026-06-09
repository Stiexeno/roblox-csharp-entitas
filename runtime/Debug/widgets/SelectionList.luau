-- Selectable row list. Each item: { text, icon?, sideText?, selected?,
-- barWidth?, ... }. Returns a handle with :selected(), :rightClicked(),
-- :hovered() — matching Matter's selectionList contract so the rest of
-- the UI ports cleanly.

return function(debugger, items)
	local plasma = debugger.plasma
	local create = plasma.create
	items = items or {}

	local selectedItem, setSelectedItem = plasma.useState()
	local rightClickedItem, setRightClickedItem = plasma.useState()
	local hoveredItem, setHoveredItem = plasma.useState()

	plasma.widget(function()
		plasma.useInstance(function()
			return create("Frame", {
				BackgroundTransparency = 1,
				Size = UDim2.new(1, 0, 0, 0),
				AutomaticSize = Enum.AutomaticSize.Y,
				[create] = {
					create("UIListLayout", {
						FillDirection = Enum.FillDirection.Vertical,
						Padding = UDim.new(0, 2),
						SortOrder = Enum.SortOrder.LayoutOrder,
					}),
				},
			})
		end)

		for index, item in ipairs(items) do
			plasma.widget(function()
				local row = plasma.useInstance(function(ref)
					local rowFrame = create("TextButton", {
						BackgroundColor3 = item.selected and Color3.fromRGB(189, 81, 92) or Color3.fromRGB(40, 40, 46),
						BackgroundTransparency = item.selected and 0.0 or 0.15,
						BorderSizePixel = 0,
						Size = UDim2.new(1, 0, 0, 24),
						Text = "",
						AutoButtonColor = false,
						[create] = {
							create("UICorner", { CornerRadius = UDim.new(0, 4) }),
							create("UIListLayout", {
								FillDirection = Enum.FillDirection.Horizontal,
								VerticalAlignment = Enum.VerticalAlignment.Center,
								Padding = UDim.new(0, 6),
								SortOrder = Enum.SortOrder.LayoutOrder,
							}),
							create("UIPadding", {
								PaddingLeft = UDim.new(0, 8),
								PaddingRight = UDim.new(0, 8),
							}),
						},
					})
					ref.row = rowFrame

					rowFrame.MouseButton1Click:Connect(function() setSelectedItem(item) end)
					rowFrame.MouseButton2Click:Connect(function() setRightClickedItem(item) end)
					rowFrame.MouseEnter:Connect(function() setHoveredItem(item) end)
					rowFrame.MouseLeave:Connect(function()
						if hoveredItem == item then setHoveredItem(nil) end
					end)
					return rowFrame
				end)

				-- Bar-width visualization (for systems with timing): fills
				-- behind the row content proportional to the rolling avg.
				if item.barWidth and item.barWidth > 0 then
					plasma.useInstance(function()
						return create("Frame", {
							Parent = row,
							BackgroundColor3 = Color3.fromRGB(189, 81, 92),
							BackgroundTransparency = 0.7,
							BorderSizePixel = 0,
							Size = UDim2.new(item.barWidth, 0, 1, 0),
							ZIndex = 0,
						})
					end)
				end

				if item.icon then plasma.label(item.icon) end
				plasma.label(item.text or "")
				if item.sideText then plasma.label(item.sideText) end
			end)
		end
	end)

	return {
		selected = function()
			local s = selectedItem
			setSelectedItem(nil)
			return s
		end,
		rightClicked = function()
			local s = rightClickedItem
			setRightClickedItem(nil)
			return s
		end,
		hovered = function() return hoveredItem end,
	}
end
