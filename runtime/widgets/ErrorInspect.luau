-- Last captured pcall error for the inspected system. The profiler
-- caches errors keyed by system; Debugger reads the union across feature
-- groups for the current debugSystem.

local CodeText = require(script.Parent.CodeText)

return function(debugger)
	local plasma = debugger.plasma
	local system = debugger.debugSystem
	if system == nil then return end

	local errorText
	for _, fg in ipairs(debugger.featureGroups) do
		local e = fg.profiler:Errors()[system]
		if e ~= nil then errorText = e break end
	end
	if errorText == nil then return end

	local closed = plasma.window({
		title = "System error",
		closable = true,
	}, function()
		CodeText(debugger, errorText)
	end):closed()

	return closed
end
