-- Floating preview of the hovered entity (alt+mouseover). Smaller than
-- EntityInspect: just the entity id and component count, anchored near
-- the mouse via Tooltip's positioning.

local Tooltip = require(script.Parent.Tooltip)

return function(debugger, context, creationIndex)
	if creationIndex == nil then return end
	local entities = context:GetEntities()
	local entity
	for i = 1, #entities do
		if entities[i]:creationIndex() == creationIndex then entity = entities[i] break end
	end
	if entity == nil then return end

	Tooltip(debugger, string.format("<b>entity #%d</b>  %d components", entity:creationIndex(), entity:totalComponents()))
end
