-- Top-level ui(debugger). Called each tick from inside plasma.beginFrame,
-- so plasma's immediate-mode tree is built per frame. Adapted from
-- matter-ecs/matter lib/debugger/ui.luau — same two-column shape (left
-- sidebar with state + systems, right area with conditionally-shown
-- inspect windows).
--
-- Mapping: Matter._worlds → debugger.contexts (each tagged with its
-- ComponentsLookup); Matter._orderedSystemsByEvent → debugger.featureGroups
-- (each FeatureGroup has a profiler + features array, and each Feature
-- holds the per-phase system arrays in Systems._executeSystems etc.).

local Container = require(script.Parent.widgets.Container)
local Panel = require(script.Parent.widgets.Panel)
local SelectionList = require(script.Parent.widgets.SelectionList)
local RealmSwitch = require(script.Parent.widgets.RealmSwitch)
local ContextInspect = require(script.Parent.widgets.ContextInspect)
local EntityInspect = require(script.Parent.widgets.EntityInspect)
local ValueInspect = require(script.Parent.widgets.ValueInspect)
local GroupInspect = require(script.Parent.widgets.GroupInspect)
local HooksInspect = require(script.Parent.widgets.HooksInspect)
local QueryInspect = require(script.Parent.widgets.QueryInspect)
local ErrorInspect = require(script.Parent.widgets.ErrorInspect)
local Tooltip = require(script.Parent.widgets.Tooltip)
local HoverInspect = require(script.Parent.widgets.HoverInspect)
local SystemNameCache = require(script.Parent.SystemNameCache)
local RollingAverage = require(script.Parent.RollingAverage)

local PRIMARY = Color3.fromHex("bd515c")
local LONG_SYSTEM_NAME = 24

local timeUnits = { "s", "ms", "μs", "ns" }
local function formatDuration(duration)
	local unit = 1
	while duration < 1 and unit < #timeUnits do
		duration *= 1000
		unit += 1
	end
	return duration, timeUnits[unit]
end

-- Find which contextEntry owns the given creationIndex for hover/inspect
-- routing. Matter does this with world:contains; we walk contexts.
local function findContextForEntityId(debugger, creationIndex)
	for _, c in ipairs(debugger.contexts) do
		local entities = c.context:GetEntities()
		for i = 1, #entities do
			if entities[i]:creationIndex() == creationIndex then return c, entities[i] end
		end
	end
	return nil, nil
end

return function(debugger)
	local plasma = debugger.plasma
	plasma.setStyle({ primaryColor = PRIMARY })

	-- Per-frame query log reset. DebuggerHookContext writes here; if we
	-- don't clear, accesses accumulate across frames.
	debugger._queries = {}

	-- Persistent UI state.
	local objectStack = plasma.useState({})
	local openContextEntry, setOpenContextEntry = plasma.useState()
	local sortByDuration, setSortByDuration = plasma.useState(false)
	local groupsOpen, setGroupsOpen = plasma.useState(false)
	local hooksOpen, setHooksOpen = plasma.useState(false)
	local queriesOpen, setQueriesOpen = plasma.useState(false)
	local recordMode, setRecordMode = plasma.useState("off")

	-- Hover render — drawn first so it sits behind everything else.
	if debugger.hoverEntity ~= nil then
		local hoverCtx = findContextForEntityId(debugger, debugger.hoverEntity)
		if hoverCtx ~= nil then
			HoverInspect(debugger, hoverCtx.context, debugger.hoverEntity)
		end
	end

	local hoveredSystem

	Container(debugger, {
		direction = Enum.FillDirection.Horizontal,
		padding = 0,
	}, function()
		Panel(debugger, function()
			if RealmSwitch(debugger).clicked() then
				if not debugger:IsServerView() then debugger:SwitchToServerView()
				else debugger:SwitchToClientView() end
			end

			plasma.space(10)
			plasma.heading("CONTEXTS")
			plasma.space(6)

			local contextRows = {}
			for _, c in ipairs(debugger.contexts) do
				local name = tostring(c.context._name or "context")
				table.insert(contextRows, {
					text = name,
					sideText = string.format("%d", c.context:count()),
					selected = openContextEntry == c,
					contextEntry = c,
				})
			end
			local ctxList = SelectionList(debugger, contextRows)
			local picked = ctxList.selected()
			if picked ~= nil then
				if openContextEntry == picked.contextEntry then
					setOpenContextEntry(nil)
				else
					setOpenContextEntry(picked.contextEntry)
				end
			end

			plasma.space(10)
			plasma.row({ verticalAlignment = Enum.VerticalAlignment.Center }, function()
				plasma.heading("FEATURES")
				if plasma.button("sort: " .. (sortByDuration and "duration" or "order")):clicked() then
					setSortByDuration(not sortByDuration)
				end
			end)
			plasma.space(6)

			for _, fg in ipairs(debugger.featureGroups) do
				for _, feature in ipairs(fg.features) do
					plasma.heading(feature.name or "Feature", { font = Enum.Font.Gotham })
					plasma.space(3)

					local systems = feature._executeSystems
					local durations = {}
					local longestDuration = 0
					for i = 1, #systems do
						local d = fg.profiler:GetAverage(systems[i], "Execute")
						durations[systems[i]] = d
						if d > longestDuration then longestDuration = d end
					end

					local rows = {}
					local skip = fg.profiler:SkipSet()
					local errors = fg.profiler:Errors()
					for index, system in ipairs(systems) do
						local duration = durations[system] or 0
						local hum, unit = formatDuration(duration)
						local sideText = string.format("%.0f%s", hum, unit)
						local icon
						if duration > 0.004 then icon = "!" end
						if errors[system] ~= nil then icon = "x" end

						local barWidth = longestDuration > 0 and (duration / longestDuration) or 0
						local disabled = skip[system] == true
						local name = SystemNameCache.GetName(system)
						table.insert(rows, {
							text = disabled and ("<s>" .. name .. "</s>") or name,
							sideText = disabled and "(disabled)" or sideText,
							selected = debugger.debugSystem == system,
							system = system,
							icon = icon,
							barWidth = barWidth,
							index = index,
						})
					end

					if sortByDuration then
						table.sort(rows, function(a, b)
							return (durations[a.system] or 0) > (durations[b.system] or 0)
						end)
					end

					local sysList = SelectionList(debugger, rows)
					local picked = sysList.selected()
					local rightClicked = sysList.rightClicked()
					hoveredSystem = sysList.hovered()
					if picked ~= nil then
						if debugger.debugSystem == picked.system then
							debugger.debugSystem = nil
						else
							debugger.debugSystem = picked.system
						end
					elseif rightClicked ~= nil then
						skip[rightClicked.system] = not skip[rightClicked.system]
					end
					plasma.space(6)
				end
			end

			plasma.space(10)
			plasma.heading("RECORD")
			plasma.space(4)
			if plasma.button("mode: " .. recordMode):clicked() then
				local next_ = recordMode == "off" and "delta" or (recordMode == "delta" and "full" or "off")
				setRecordMode(next_)
				debugger.snapshot:SetMode(next_)
			end
			plasma.label(string.format("frames: %d", debugger.snapshot:FrameCount()))
		end)

		Container(debugger, {
			marginTop = 46,
			marginLeft = 10,
			direction = Enum.FillDirection.Horizontal,
		}, function()
			if openContextEntry ~= nil then
				local closed = ContextInspect(debugger, openContextEntry)
				if closed then setOpenContextEntry(nil) end
			end

			if debugger.debugEntity ~= nil then
				local ownerCtx = findContextForEntityId(debugger, debugger.debugEntity)
				if ownerCtx ~= nil then
					EntityInspect(debugger, ownerCtx, objectStack)
				end
			end

			if #objectStack > 0 then
				ValueInspect(debugger, objectStack)
			end

			if debugger.debugSystem ~= nil then
				local name = SystemNameCache.GetName(debugger.debugSystem)
				local closed = plasma.window({
					title = "System",
					closable = true,
				}, function()
					plasma.useKey(name)
					if plasma.button(string.format("Queries (%d)", #debugger._queries)):clicked() then
						setQueriesOpen(true)
					end
					local disabled = false
					for _, fg in ipairs(debugger.featureGroups) do
						if fg.profiler:SkipSet()[debugger.debugSystem] then disabled = true break end
					end
					if plasma.checkbox("<font size='13'>Disable System</font>", { checked = disabled }):clicked() then
						for _, fg in ipairs(debugger.featureGroups) do
							local skip = fg.profiler:SkipSet()
							skip[debugger.debugSystem] = not disabled
						end
					end
				end):closed()
				if closed then debugger.debugSystem = nil end

				if queriesOpen then
					local closedQ = QueryInspect(debugger)
					if closedQ then setQueriesOpen(false) end
				end

				ErrorInspect(debugger)
			end

			if groupsOpen and openContextEntry ~= nil then
				GroupInspect(debugger, openContextEntry)
			end

			if hooksOpen and openContextEntry ~= nil then
				HooksInspect(debugger, openContextEntry)
			end

			-- Below-the-fold toggles for groups/hooks panes.
			plasma.useKey("contextpanes")
			plasma.row({}, function()
				if plasma.button(groupsOpen and "hide groups" or "show groups"):clicked() then
					setGroupsOpen(not groupsOpen)
				end
				if plasma.button(hooksOpen and "hide hooks" or "show hooks"):clicked() then
					setHooksOpen(not hooksOpen)
				end
			end)
		end)
	end)

	if hoveredSystem ~= nil and hoveredSystem.system ~= nil then
		local name = SystemNameCache.GetName(hoveredSystem.system)
		if #name >= LONG_SYSTEM_NAME then
			Tooltip(debugger, "<b>" .. name .. "</b>")
		end
	end
end
