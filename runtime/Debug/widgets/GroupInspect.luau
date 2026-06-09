-- Window for a context's groups (cached IMatcher → Group). Each row:
-- matcher index breakdown + current member count. Clicking a group sets
-- it as the inspected one and lists members.

local SelectionList = require(script.Parent.SelectionList)

local function describeMatcher(matcher, lookup)
	if matcher == nil then return "?" end
	local parts = {}
	local allOf = matcher:allOfIndices()
	if allOf and #allOf > 0 then
		local names = {}
		for i = 1, #allOf do table.insert(names, lookup and lookup[allOf[i] + 1] or tostring(allOf[i])) end
		table.insert(parts, "AllOf(" .. table.concat(names, ",") .. ")")
	end
	local anyOf = matcher:anyOfIndices()
	if anyOf and #anyOf > 0 then
		local names = {}
		for i = 1, #anyOf do table.insert(names, lookup and lookup[anyOf[i] + 1] or tostring(anyOf[i])) end
		table.insert(parts, "AnyOf(" .. table.concat(names, ",") .. ")")
	end
	local noneOf = matcher:noneOfIndices()
	if noneOf and #noneOf > 0 then
		local names = {}
		for i = 1, #noneOf do table.insert(names, lookup and lookup[noneOf[i] + 1] or tostring(noneOf[i])) end
		table.insert(parts, "NoneOf(" .. table.concat(names, ",") .. ")")
	end
	return table.concat(parts, " ")
end

return function(debugger, contextEntry)
	local plasma = debugger.plasma
	local context = contextEntry.context
	local lookup = contextEntry.lookup

	local closed = plasma.window({
		title = "Groups",
		closable = true,
	}, function()
		local rows = {}
		for _, group in pairs(context._groups or {}) do
			table.insert(rows, {
				text = describeMatcher(group:matcher(), lookup),
				sideText = string.format("%d", group:count()),
				group = group,
			})
		end
		if #rows == 0 then
			plasma.label("(no groups cached)")
			return
		end

		local list = SelectionList(debugger, rows)
		local selected = list.selected()
		if selected ~= nil then
			debugger.inspectedGroup = selected.group
		end

		if debugger.inspectedGroup ~= nil then
			plasma.space(8)
			plasma.heading("Members")
			local entityRows = {}
			local entities = debugger.inspectedGroup:GetEntities()
			for i = 1, #entities do
				table.insert(entityRows, {
					text = string.format("entity #%d", entities[i]:creationIndex()),
					creationIndex = entities[i]:creationIndex(),
				})
			end
			local memberList = SelectionList(debugger, entityRows)
			local picked = memberList.selected()
			if picked ~= nil then debugger.debugEntity = picked.creationIndex end
		end
	end):closed()

	if closed then debugger.inspectedGroup = nil end
end
