-- Full-height column showing one entity's components with editable
-- fields. Each component shows as a section: name + each field with an
-- EditableValue row. Mutations write back via entity:ReplaceComponent.

local EditableValue = require(script.Parent.EditableValue)

local function findEntity(context, creationIndex)
	local entities = context:GetEntities()
	for i = 1, #entities do
		if entities[i]:creationIndex() == creationIndex then return entities[i] end
	end
	return nil
end

return function(debugger, panelEntry, onClose)
	local plasma = debugger.plasma
	local create = plasma.create

	-- panelEntry.entity may be a captured reference, but for pool-reuse
	-- safety we resolve by creationIndex each frame.
	local entity = panelEntry.entity
	if entity == nil or not entity:isEnabled() then
		-- Resolve again in case the entity was destroyed and the panel
		-- should close.
		entity = findEntity(panelEntry.context, panelEntry.creationIndex or -1)
		if entity == nil then
			onClose(panelEntry)
			return
		end
	end
	-- Capture creationIndex for the close-on-destroyed path next frame.
	panelEntry.creationIndex = entity:creationIndex()

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
			plasma.useInstance(function(ref)
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
						Text = string.format("Entity #%d", entity:creationIndex()),
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

		-- Body
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

			local indices = entity:GetComponentIndices()
			-- Stable order; sort ascending so layout doesn't jump between ticks.
			table.sort(indices)

			for _, idx in ipairs(indices) do
				local component = entity:GetComponent(idx)
				local name = panelEntry.lookup and (panelEntry.lookup[idx + 1] or panelEntry.lookup[idx])
				name = name or ("component " .. tostring(idx))

				plasma.useKey("c-" .. tostring(idx))
				plasma.scope(function()
					-- Component header
					plasma.useInstance(function()
						return create("TextLabel", {
							BackgroundColor3 = Color3.fromRGB(34, 34, 38),
							BorderSizePixel = 0,
							Size = UDim2.new(1, 0, 0, 22),
							Font = Enum.Font.GothamBold,
							TextSize = 11,
							TextColor3 = Color3.fromRGB(180, 180, 190),
							TextXAlignment = Enum.TextXAlignment.Left,
							Text = "  " .. name,
						})
					end)

					-- Fields. Flag components have no fields → show one read-only row.
					local hasAnyField = false
					if type(component) == "table" then
						for fieldName, fieldValue in pairs(component) do
							if string.sub(tostring(fieldName), 1, 1) ~= "_" then
								hasAnyField = true
								plasma.useKey("f-" .. tostring(idx) .. "-" .. tostring(fieldName))
								plasma.scope(function()
									EditableValue(debugger, tostring(fieldName), fieldValue, function(newValue)
										-- Mutate in-place; the codegen-emitted Replace
										-- pattern is `e:ReplacePosition(component)` which
										-- pushes the displaced component to the pool. We
										-- skip that round-trip and just mutate — the
										-- group notifier still fires from the next
										-- Add/Replace/Remove the user code performs.
										-- (Trade-off documented; sufficient for the
										-- in-game debugger.)
										component[fieldName] = newValue
									end)
								end)
							end
						end
					end
					if not hasAnyField then
						plasma.useKey("flag-" .. tostring(idx))
						plasma.scope(function()
							plasma.useInstance(function()
								return create("TextLabel", {
									BackgroundTransparency = 1,
									Size = UDim2.new(1, 0, 0, 18),
									Font = Enum.Font.Code,
									TextSize = 10,
									TextColor3 = Color3.fromRGB(160, 160, 170),
									TextXAlignment = Enum.TextXAlignment.Left,
									Text = "    (flag)",
								})
							end)
						end)
					end
				end)
			end
		end)
	end)
end
