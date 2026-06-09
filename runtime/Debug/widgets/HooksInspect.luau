-- Inspector for the codegen-emitted I{Ctx}{Component}ContextHooks
-- bookkeeping. These interfaces are only generated for [Unique],
-- [EntityIndex], or [PrimaryEntityIndex] components, and the context's
-- partial class implements them — so the context IS the subscriber.
--
-- At runtime there's no general "hooks subscribers" registry; the
-- "state" is whatever the context's private fields hold (e.g., for
-- [Unique] components the context has a *Entity field). v1 shows a
-- summary: which components on this context have hook-tagged state, and
-- for [Unique] components their current bound entity creationIndex.
-- Discovery is via field-name heuristic since reflection isn't free in
-- transpiled Luau.

return function(debugger, contextEntry)
	local plasma = debugger.plasma
	local context = contextEntry.context
	local lookup = contextEntry.lookup

	local closed = plasma.window({
		title = "Hooks",
		closable = true,
	}, function()
		plasma.label("Discovered hook-tagged state on this context:")
		plasma.space(4)

		local count = 0
		-- Walk context keys; private hook fields are conventionally named
		-- `_unique{Component}Entity` / `_index{Component}` per the
		-- ContextPartialTemplate. Show whatever matches.
		for key, value in pairs(context) do
			if type(key) == "string" then
				local isUnique = string.find(key, "^_unique") ~= nil
				local isIndex = string.find(key, "^_index") ~= nil
				if isUnique or isIndex then
					count = count + 1
					local desc
					if isUnique and type(value) == "table" and value.creationIndex then
						desc = string.format("→ entity #%d", value:creationIndex())
					elseif isIndex and type(value) == "table" then
						local n = 0
						for _ in pairs(value) do n = n + 1 end
						desc = string.format("→ %d keys", n)
					else
						desc = "(empty)"
					end
					plasma.label(string.format("  %s  %s", key, desc))
				end
			end
		end

		if count == 0 then
			plasma.label("(no hook-tagged state on this context)")
		end

		-- The lookup itself documents which components COULD be hook-tagged;
		-- show it for cross-reference.
		plasma.space(6)
		plasma.heading("Component lookup", { font = Enum.Font.Gotham })
		plasma.space(4)
		if lookup ~= nil then
			for i, name in ipairs(lookup) do
				plasma.label(string.format("  [%d] %s", i - 1, tostring(name)))
			end
		end
	end):closed()

	return closed
end
