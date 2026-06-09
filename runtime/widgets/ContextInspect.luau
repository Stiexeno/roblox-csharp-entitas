-- Window for a selected Context. Shows entity count + scrollable entity
-- list; clicking an entity sets debugger.debugEntity to its creationIndex
-- which EntityInspect picks up.

local SelectionList = require(script.Parent.SelectionList)

return function(debugger, contextEntry)
	local plasma = debugger.plasma
	local context = contextEntry.context

	local closed = plasma.window({
		title = string.format("Context (%d entities)", context:count()),
		closable = true,
	}, function()
		plasma.space(4)

		local rows = {}
		local entities = context:GetEntities()
		for i = 1, #entities do
			local e = entities[i]
			local selected = debugger.debugEntity == e:creationIndex()
			table.insert(rows, {
				text = string.format("entity #%d", e:creationIndex()),
				sideText = string.format("%d comp", e:totalComponents()),
				selected = selected,
				creationIndex = e:creationIndex(),
			})
		end

		local list = SelectionList(debugger, rows)
		local selected = list.selected()
		if selected ~= nil then
			debugger.debugEntity = selected.creationIndex
		end
	end):closed()

	return closed
end
