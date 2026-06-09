-- Window for the currently-selected entity. Lists components by index,
-- shows component-name (from componentsLookup) and a brief value summary;
-- clicking a component pushes it onto the object stack for drill-down.

local FormatTable = require(script.Parent.Parent.FormatTable)
local SelectionList = require(script.Parent.SelectionList)

local function findEntityByCreationIndex(context, creationIndex)
	if creationIndex == nil then return nil end
	local entities = context:GetEntities()
	for i = 1, #entities do
		if entities[i]:creationIndex() == creationIndex then return entities[i] end
	end
	return nil
end

return function(debugger, contextEntry, objectStack)
	local plasma = debugger.plasma
	local context = contextEntry.context
	local lookup = contextEntry.lookup

	local entity = findEntityByCreationIndex(context, debugger.debugEntity)
	if entity == nil then return end

	local closed = plasma.window({
		title = string.format("Entity #%d", entity:creationIndex()),
		closable = true,
	}, function()
		plasma.label(string.format("components: %d", entity:totalComponents()))
		plasma.space(6)

		local rows = {}
		local indices = entity:GetComponentIndices()
		for i = 1, #indices do
			local idx = indices[i]
			local component = entity:GetComponent(idx)
			local name = lookup and lookup[idx + 1] or ("component " .. tostring(idx))
			-- lookup is 0-indexed by componentIndex on the C# side; Lua
			-- arrays are 1-indexed, so the codegen-emitted componentNames
			-- ends up as a 1-based table. If consumer passes a 0-based
			-- table, fall back to direct index. Try both.
			if name == nil and lookup ~= nil then name = lookup[idx] end
			name = name or ("component " .. tostring(idx))

			local sideText = type(component) == "table" and "{…}" or FormatTable(component)
			table.insert(rows, {
				text = name,
				sideText = sideText,
				value = component,
				key = name,
			})
		end

		local list = SelectionList(debugger, rows)
		local selected = list.selected()
		if selected ~= nil then
			-- Replace the stack with just this component (consistent with
			-- Matter's "click a component to inspect it from scratch").
			table.clear(objectStack)
			objectStack[1] = { key = selected.key, value = selected.value }
		end
	end):closed()

	if closed then debugger.debugEntity = nil end
end
