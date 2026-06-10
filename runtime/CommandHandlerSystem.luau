-- CommandHandlerSystem base. Mirrors frozen-feast's RequestHandlerSystem
-- shape: subclasses override OnExecute, the base wraps it with a short-
-- circuit on empty groups and an auto-destroy sweep on the way out.
--
-- The reusable _requestBuffer is the perf-critical trick. Group:GetEntities
-- supports a clear-and-fill buffer overload — we hand back the same array
-- every frame so the destroy loop doesn't allocate per execute. Subclasses
-- can opt out of the destroy via ShouldCancelRequestDeletion = true inside
-- OnExecute (rare; mostly useful when an entity must survive into next
-- frame for an event-shape consumer).

local CommandHandlerSystem = {}
CommandHandlerSystem.__index = CommandHandlerSystem

setmetatable(CommandHandlerSystem, {
	__tostring = function() return "CommandHandlerSystem" end,
})

function CommandHandlerSystem.new(requestGroup)
	local self = setmetatable({}, CommandHandlerSystem)
	return self:constructor(requestGroup) or self
end

CommandHandlerSystem.__ctorParams = {}
CommandHandlerSystem.__interfaces = { "IExecuteSystem" }

function CommandHandlerSystem:constructor(requestGroup)
	self._requests = requestGroup
	self._requestBuffer = {}
	self.ShouldCancelRequestDeletion = false
end

function CommandHandlerSystem:Execute()
	if self._requests:count() == 0 then return end

	self:OnExecute(self._requests)

	if self.ShouldCancelRequestDeletion then return end

	local buf = self._requests:GetEntities(self._requestBuffer)
	for i = 1, #buf do
		buf[i]:Destroy()
	end
end

-- Subclasses MUST override. The error here only fires if a user
-- instantiates the base class directly, which the C# abstract marker
-- already prevents at compile time.
function CommandHandlerSystem:OnExecute(_requests)
	error("CommandHandlerSystem:OnExecute is abstract — override in subclass.")
end

return CommandHandlerSystem
