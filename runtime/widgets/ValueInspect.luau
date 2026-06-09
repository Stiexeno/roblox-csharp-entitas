-- Drill-down inspector for an arbitrary value. Stack-based: clicking a
-- table value pushes a new entry; the close button pops. Uses FormatTable
-- to pretty-print leaves.

local FormatTable = require(script.Parent.Parent.FormatTable)

-- formatTable only handles Lua tables; Vector3/Color3/CFrame/etc are
-- userdata. tostring works for those AND for primitives.
local function describe(v)
	if type(v) == "table" then return FormatTable.formatTable(v) end
	return tostring(v)
end

return function(debugger, objectStack)
	local plasma = debugger.plasma
	if #objectStack == 0 then return end

	local top = objectStack[#objectStack]
	local closed = plasma.window({
		title = tostring(top.key or "Value"),
		closable = true,
	}, function()
		local value = top.value
		if type(value) == "table" then
			local rows = {}
			for k, v in pairs(value) do
				local sideText
				if type(v) == "table" then
					sideText = "{…}"
				else
					sideText = describe(v)
				end
				table.insert(rows, { text = tostring(k), sideText = sideText, value = v, key = k })
			end
			local list = require(script.Parent.SelectionList)(debugger, rows)
			local selected = list.selected()
			if selected and type(selected.value) == "table" then
				table.insert(objectStack, {
					key = tostring(selected.key),
					value = selected.value,
				})
			end
		else
			plasma.label(describe(value))
		end
	end):closed()

	if closed then
		objectStack[#objectStack] = nil
	end
end
