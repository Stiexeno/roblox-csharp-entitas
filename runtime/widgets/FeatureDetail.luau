-- Full-height column panel showing one feature's systems, grouped by
-- phase. Each row: checkbox (skip), system name, Ø ms (avg) / ▼ min / ▲ max.
-- Header has a close X that removes this panel from debugger.openPanels.
--
-- Uses plasma.useInstance's ref-table return so live values (stats) can be
-- mutated each frame without recreating the row.

local SystemNameCache = require(script.Parent.Parent.SystemNameCache)

local function formatMs(seconds)
	return string.format("%6.3f", seconds * 1000)
end

local PHASE_ORDER = { "Initialize", "Execute", "Cleanup", "TearDown" }
local PHASE_FIELDS = {
	Initialize = "_initializeSystems",
	Execute = "_executeSystems",
	Cleanup = "_cleanupSystems",
	TearDown = "_tearDownSystems",
}

local function renderRow(plasma, profiler, system, phase)
	local create = plasma.create
	local refs = plasma.useInstance(function(ref)
		local row = create("Frame", {
			[ref] = "row",
			BackgroundColor3 = Color3.fromRGB(40, 40, 46),
			BorderSizePixel = 0,
			Size = UDim2.new(1, 0, 0, 22),
			create("UIListLayout", {
				FillDirection = Enum.FillDirection.Horizontal,
				VerticalAlignment = Enum.VerticalAlignment.Center,
				Padding = UDim.new(0, 4),
				SortOrder = Enum.SortOrder.LayoutOrder,
			}),
			create("UIPadding", {
				PaddingLeft = UDim.new(0, 6),
				PaddingRight = UDim.new(0, 6),
			}),
			create("TextButton", {
				[ref] = "checkbox",
				BackgroundColor3 = Color3.fromRGB(60, 60, 66),
				BorderSizePixel = 0,
				Size = UDim2.new(0, 14, 0, 14),
				Font = Enum.Font.GothamBold,
				TextSize = 10,
				TextColor3 = Color3.fromRGB(220, 220, 220),
				Text = "",
				LayoutOrder = 1,
				AutoButtonColor = false,
			}),
			create("TextLabel", {
				[ref] = "name",
				BackgroundTransparency = 1,
				Size = UDim2.new(1, -200, 1, 0),
				Font = Enum.Font.Gotham,
				TextSize = 11,
				TextColor3 = Color3.fromRGB(220, 220, 220),
				TextXAlignment = Enum.TextXAlignment.Left,
				Text = "",
				LayoutOrder = 2,
			}),
			create("TextLabel", {
				[ref] = "stats",
				BackgroundTransparency = 1,
				Size = UDim2.new(0, 180, 1, 0),
				Font = Enum.Font.Code,
				TextSize = 10,
				TextColor3 = Color3.fromRGB(180, 180, 190),
				TextXAlignment = Enum.TextXAlignment.Right,
				Text = "",
				LayoutOrder = 3,
			}),
		})

		ref.checkbox.MouseButton1Click:Connect(function()
			local set = profiler:SkipSet()
			set[system] = not set[system]
		end)

		return row
	end)

	-- Per-frame updates.
	local skip = profiler:SkipSet()
	refs.checkbox.Text = skip[system] and "" or "✓"
	refs.name.Text = SystemNameCache.GetName(system)
	local avg, lo, hi = profiler:GetStats(system, phase)
	refs.stats.Text = string.format("Ø%s ▼%s ▲%s", formatMs(avg), formatMs(lo), formatMs(hi))
end

return function(debugger, panelEntry, onClose)
	local plasma = debugger.plasma
	local create = plasma.create
	local feature = panelEntry.feature
	local profiler = panelEntry.profiler

	plasma.scope(function()
		plasma.useInstance(function()
			return create("Frame", {
				BackgroundColor3 = Color3.fromRGB(28, 28, 32),
				BorderSizePixel = 0,
				Size = UDim2.new(0, 350, 1, 0),
				create("UIListLayout", {
					FillDirection = Enum.FillDirection.Vertical,
					SortOrder = Enum.SortOrder.LayoutOrder,
				}),
			})
		end)

		-- Header
		plasma.scope(function()
			local refs = plasma.useInstance(function(ref)
				local frame = create("Frame", {
					BackgroundColor3 = Color3.fromRGB(20, 20, 24),
					BorderSizePixel = 0,
					Size = UDim2.new(1, 0, 0, 36),
					LayoutOrder = 1,
					create("TextLabel", {
						BackgroundTransparency = 1,
						Position = UDim2.new(0, 12, 0, 0),
						Size = UDim2.new(1, -36, 1, 0),
						Font = Enum.Font.GothamBold,
						TextSize = 12,
						TextColor3 = Color3.fromRGB(220, 220, 220),
						TextXAlignment = Enum.TextXAlignment.Left,
						Text = feature.name or "Feature",
					}),
					create("TextButton", {
						[ref] = "closeBtn",
						BackgroundTransparency = 1,
						Position = UDim2.new(1, -32, 0, 6),
						Size = UDim2.new(0, 24, 0, 24),
						Font = Enum.Font.GothamBold,
						TextSize = 16,
						TextColor3 = Color3.fromRGB(220, 220, 220),
						Text = "✕",
						AutoButtonColor = false,
					}),
				})
				ref.closeBtn.Activated:Connect(function() onClose(panelEntry) end)
				return frame
			end)
		end)

		-- Body (scrolling)
		plasma.scope(function()
			plasma.useInstance(function()
				return create("ScrollingFrame", {
					BackgroundTransparency = 1,
					BorderSizePixel = 0,
					Size = UDim2.new(1, 0, 1, -36),
					CanvasSize = UDim2.new(0, 0, 0, 0),
					AutomaticCanvasSize = Enum.AutomaticSize.Y,
					ScrollBarThickness = 4,
					LayoutOrder = 2,
					create("UIListLayout", {
						FillDirection = Enum.FillDirection.Vertical,
						Padding = UDim.new(0, 0),
						SortOrder = Enum.SortOrder.LayoutOrder,
					}),
				})
			end)

			for _, phase in ipairs(PHASE_ORDER) do
				local list = feature[PHASE_FIELDS[phase]]
				if list ~= nil and #list > 0 then
					plasma.useKey("hdr-" .. phase)
					plasma.scope(function()
						plasma.useInstance(function()
							return create("TextLabel", {
								BackgroundColor3 = Color3.fromRGB(34, 34, 38),
								BorderSizePixel = 0,
								Size = UDim2.new(1, 0, 0, 22),
								Font = Enum.Font.GothamBold,
								TextSize = 11,
								TextColor3 = Color3.fromRGB(180, 180, 190),
								TextXAlignment = Enum.TextXAlignment.Left,
								Text = "  " .. string.upper(phase) .. " SYSTEMS",
							})
						end)
					end)

					for index, system in ipairs(list) do
						plasma.useKey(phase .. "-" .. tostring(index))
						plasma.scope(function()
							renderRow(plasma, profiler, system, phase)
						end)
					end
				end
			end
		end)
	end)
end
