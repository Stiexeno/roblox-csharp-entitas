-- Per-frame group-access log for the inspected system. Populated by
-- DebuggerHookContext.patchedGetEntities each time the inspected system
-- iterates a group. Reset each frame by DebuggerUi.

return function(debugger)
	local plasma = debugger.plasma

	local queries = debugger._queries
	if queries == nil or #queries == 0 then
		return plasma.window({
			title = "Queries",
			closable = true,
		}, function()
			plasma.label("(no group accesses recorded this frame for the selected system)")
		end):closed()
	end

	local closed = plasma.window({
		title = string.format("Queries (%d)", #queries),
		closable = true,
	}, function()
		for i = 1, #queries do
			local q = queries[i]
			local avgMicros = (q.averageDuration or 0) * 1e6
			plasma.label(string.format("group#%d  count=%d  ~%.1fμs", i, q.count or 0, avgMicros))
		end
	end):closed()

	return closed
end
