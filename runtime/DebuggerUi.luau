-- Column-stack UI. Sidebar pinned left (260px), then for each entry in
-- debugger.openPanels we render a 350px full-height detail column.
-- Replaces the previous plasma.window-based floating layout.

local Sidebar = require(script.Parent.widgets.Sidebar)
local FeatureDetail = require(script.Parent.widgets.FeatureDetail)
local EntityDetail = require(script.Parent.widgets.EntityDetail)

local PANELS_BY_TYPE = {
	feature = FeatureDetail,
	entity = EntityDetail,
}

return function(debugger)
	debugger.openPanels = debugger.openPanels or {}

	-- Per-frame query log reset (kept for the HookContext shape even
	-- though the QueryInspect window is gone from v2 UI).
	debugger._queries = {}

	local plasma = debugger.plasma
	local create = plasma.create

	local function closePanel(target)
		for i = #debugger.openPanels, 1, -1 do
			if debugger.openPanels[i] == target then
				table.remove(debugger.openPanels, i)
				return
			end
		end
	end

	plasma.scope(function()
		plasma.useInstance(function()
			return create("Frame", {
				BackgroundTransparency = 1,
				Size = UDim2.fromScale(1, 1),
				create("UIListLayout", {
					FillDirection = Enum.FillDirection.Horizontal,
					HorizontalAlignment = Enum.HorizontalAlignment.Left,
					VerticalAlignment = Enum.VerticalAlignment.Top,
					Padding = UDim.new(0, 0),
					SortOrder = Enum.SortOrder.LayoutOrder,
				}),
			})
		end)

		Sidebar(debugger)

		for i, panel in ipairs(debugger.openPanels) do
			local renderer = PANELS_BY_TYPE[panel.type]
			if renderer ~= nil then
				plasma.useKey(panel.type .. "-" .. tostring(i))
				plasma.scope(function()
					renderer(debugger, panel, closePanel)
				end)
			end
		end
	end)
end
