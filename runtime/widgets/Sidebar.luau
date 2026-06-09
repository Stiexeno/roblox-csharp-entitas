-- Left sidebar: 260px full-height, tabbed (Features / Entities), with a
-- list below. Selecting a row pushes a detail panel onto
-- debugger.openPanels which the orchestrator (DebuggerUi) renders to
-- the right.

local function matchesSearch(query, ...)
	if query == nil or query == "" then return true end
	local lower = string.lower(query)
	for i = 1, select("#", ...) do
		local candidate = select(i, ...)
		if candidate ~= nil and string.find(string.lower(tostring(candidate)), lower, 1, true) ~= nil then
			return true
		end
	end
	return false
end

return function(debugger)
	local plasma = debugger.plasma
	local create = plasma.create

	local activeTab, setActiveTab = plasma.useState("features")
	local search, setSearch = plasma.useState("")

	plasma.scope(function()
		plasma.useInstance(function()
			return create("Frame", {
				BackgroundColor3 = Color3.fromRGB(28, 28, 32),
				BorderSizePixel = 0,
				Size = UDim2.new(0, 260, 1, 0),
				create("UIListLayout", {
					FillDirection = Enum.FillDirection.Vertical,
					Padding = UDim.new(0, 0),
					SortOrder = Enum.SortOrder.LayoutOrder,
				}),
			})
		end)

		-- Header
		plasma.scope(function()
			plasma.useInstance(function()
				return create("Frame", {
					BackgroundColor3 = Color3.fromRGB(20, 20, 24),
					BorderSizePixel = 0,
					Size = UDim2.new(1, 0, 0, 36),
					create("TextLabel", {
						BackgroundTransparency = 1,
						Size = UDim2.new(1, 0, 1, 0),
						Font = Enum.Font.GothamBold,
						TextSize = 12,
						TextColor3 = Color3.fromRGB(220, 220, 220),
						TextXAlignment = Enum.TextXAlignment.Center,
						Text = "ENTITIES DEBUGGER",
					}),
				})
			end)
		end)

		-- Tab strip
		plasma.scope(function()
			plasma.useInstance(function()
				return create("Frame", {
					BackgroundTransparency = 1,
					Size = UDim2.new(1, 0, 0, 30),
					create("UIListLayout", {
						FillDirection = Enum.FillDirection.Horizontal,
						SortOrder = Enum.SortOrder.LayoutOrder,
					}),
				})
			end)

			for _, tab in ipairs({ "features", "entities" }) do
				plasma.useKey(tab)
				plasma.scope(function()
					plasma.useInstance(function()
						local btn = create("TextButton", {
							BackgroundColor3 = activeTab == tab and Color3.fromRGB(189, 81, 92) or Color3.fromRGB(40, 40, 46),
							BorderSizePixel = 0,
							Size = UDim2.new(0.5, 0, 1, 0),
							Font = Enum.Font.Gotham,
							TextSize = 12,
							TextColor3 = Color3.fromRGB(220, 220, 220),
							Text = string.upper(tab),
							AutoButtonColor = false,
						})
						btn.MouseButton1Click:Connect(function() setActiveTab(tab) end)
						return btn
					end)
				end)
			end
		end)

		-- Body: scrolling list
		plasma.scope(function()
			plasma.useInstance(function()
				return create("ScrollingFrame", {
					BackgroundTransparency = 1,
					BorderSizePixel = 0,
					Size = UDim2.new(1, 0, 1, -66),
					CanvasSize = UDim2.new(0, 0, 0, 0),
					AutomaticCanvasSize = Enum.AutomaticSize.Y,
					ScrollBarThickness = 4,
					create("UIListLayout", {
						FillDirection = Enum.FillDirection.Vertical,
						Padding = UDim.new(0, 2),
						SortOrder = Enum.SortOrder.LayoutOrder,
					}),
					create("UIPadding", {
						PaddingLeft = UDim.new(0, 6),
						PaddingRight = UDim.new(0, 6),
						PaddingTop = UDim.new(0, 6),
						PaddingBottom = UDim.new(0, 6),
					}),
				})
			end)

			if activeTab == "features" then
				for _, fg in ipairs(debugger.featureGroups) do
					for _, feature in ipairs(fg.features) do
						local name = feature.name or "Feature"
						plasma.useKey(tostring(feature))
						plasma.scope(function()
							plasma.useInstance(function()
								local row = create("TextButton", {
									BackgroundColor3 = Color3.fromRGB(40, 40, 46),
									BorderSizePixel = 0,
									Size = UDim2.new(1, 0, 0, 28),
									Font = Enum.Font.Gotham,
									TextSize = 12,
									TextColor3 = Color3.fromRGB(220, 220, 220),
									TextXAlignment = Enum.TextXAlignment.Left,
									Text = "  " .. name,
									AutoButtonColor = false,
									create("UICorner", { CornerRadius = UDim.new(0, 4) }),
								})
								row.MouseButton1Click:Connect(function()
									-- Replace any open feature panel with this one.
									for i = #debugger.openPanels, 1, -1 do
										if debugger.openPanels[i].type == "feature" then
											table.remove(debugger.openPanels, i)
										end
									end
									table.insert(debugger.openPanels, {
										type = "feature",
										feature = feature,
										profiler = fg.profiler,
									})
								end)
								return row
							end)
						end)
					end
				end
			else
				-- Search input
				plasma.scope(function()
					plasma.useInstance(function()
						local box = create("TextBox", {
							BackgroundColor3 = Color3.fromRGB(20, 20, 24),
							BorderSizePixel = 0,
							Size = UDim2.new(1, 0, 0, 26),
							Font = Enum.Font.Gotham,
							TextSize = 12,
							TextColor3 = Color3.fromRGB(220, 220, 220),
							PlaceholderText = "search by id or component...",
							PlaceholderColor3 = Color3.fromRGB(120, 120, 130),
							ClearTextOnFocus = false,
							Text = search,
							TextXAlignment = Enum.TextXAlignment.Left,
							create("UICorner", { CornerRadius = UDim.new(0, 4) }),
							create("UIPadding", {
								PaddingLeft = UDim.new(0, 6),
								PaddingRight = UDim.new(0, 6),
							}),
						})
						box:GetPropertyChangedSignal("Text"):Connect(function()
							setSearch(box.Text)
						end)
						return box
					end)
				end)

				-- Entity rows
				for _, c in ipairs(debugger.contexts) do
					local entities = c.context:GetEntities()
					for i = 1, #entities do
						local entity = entities[i]
						local componentNamesForSearch = ""
						if c.lookup ~= nil then
							local indices = entity:GetComponentIndices()
							for j = 1, #indices do
								local n = c.lookup[indices[j] + 1] or c.lookup[indices[j]]
								if n then componentNamesForSearch = componentNamesForSearch .. n .. " " end
							end
						end
						if matchesSearch(search, entity:creationIndex(), componentNamesForSearch) then
							plasma.useKey("e" .. tostring(entity:creationIndex()))
							plasma.scope(function()
								plasma.useInstance(function()
									local row = create("TextButton", {
										BackgroundColor3 = Color3.fromRGB(40, 40, 46),
										BorderSizePixel = 0,
										Size = UDim2.new(1, 0, 0, 24),
										Font = Enum.Font.Gotham,
										TextSize = 12,
										TextColor3 = Color3.fromRGB(220, 220, 220),
										TextXAlignment = Enum.TextXAlignment.Left,
										Text = string.format("  entity #%d  (%d)", entity:creationIndex(), entity:totalComponents()),
										AutoButtonColor = false,
										create("UICorner", { CornerRadius = UDim.new(0, 4) }),
									})
									row.MouseButton1Click:Connect(function()
										for k = #debugger.openPanels, 1, -1 do
											if debugger.openPanels[k].type == "entity" then
												table.remove(debugger.openPanels, k)
											end
										end
										table.insert(debugger.openPanels, {
											type = "entity",
											entity = entity,
											context = c.context,
											lookup = c.lookup,
										})
									end)
									return row
								end)
							end)
						end
					end
				end
			end
		end)
	end)
end
