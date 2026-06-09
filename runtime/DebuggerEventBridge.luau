-- Plasma input bridge for server view. Adapted from matter-ecs/matter
-- lib/debugger/EventBridge.luau.
--
-- The plasma runtime running on the server wants to know about UI input
-- events (button clicks, slider drags) that happen on the client's
-- rendered ScreenGui. This module is the wire:
--
--   Server side:  plasma calls bridge:connect(instance, event, handler)
--                 → bridge tells each connected player "connect to this"
--                 → when the player forwards back "event", dispatch handler.
--   Client side:  on receiving ("connect", instance, event), wire the
--                 local signal and forward fires back as ("event", ...).
--
-- InputChanged is debounced 250ms (mouse-move spam protection) — Matter's
-- threshold; preserved.

local RunService = game:GetService("RunService")

local DebuggerEventBridge = {}
DebuggerEventBridge.__index = DebuggerEventBridge

local debouncedEvents = {
	InputChanged = true,
}

local debounce = {}

local function serialize(...)
	local first = ...
	if first and typeof(first) == "Instance" and first:IsA("InputObject") then
		return {
			Delta = first.Delta,
			KeyCode = first.KeyCode,
			Position = first.Position,
			UserInputState = first.UserInputState,
			UserInputType = first.UserInputType,
		}
	end
	return ...
end

-- Client-side dispatch table. The client receives ("connect", instance,
-- event) from the server, looks up clientActions.connect, and the action
-- wires up the local signal forwarding.
local clientConnections = {}
DebuggerEventBridge.clientActions = {
	connect = function(fire, instance, event)
		local instanceFromServer = instance

		if type(instance) == "string" then
			instance = game:GetService(instance)
		end

		if clientConnections[instance] == nil then
			clientConnections[instance] = {}
		end

		clientConnections[instance][event] = instance[event]:Connect(function(...)
			if debouncedEvents[event] and not RunService:IsStudio() then
				local args = table.pack(serialize(...))

				if debounce[instance] and debounce[instance][event] then
					debounce[instance][event] = args
				else
					if debounce[instance] == nil then
						debounce[instance] = {}
					end
					debounce[instance][event] = args
					task.delay(0.25, function()
						local pending = debounce[instance][event]
						fire("event", instanceFromServer, event, table.unpack(pending, 1, pending.n))
						debounce[instance][event] = nil
						if next(debounce[instance]) == nil then debounce[instance] = nil end
					end)
				end
				return
			end
			fire("event", instanceFromServer, event, serialize(...))
		end)
	end,

	disconnect = function(_fire, instance, event)
		if type(instance) == "string" then
			instance = game:GetService(instance)
		end
		if clientConnections[instance] and clientConnections[instance][event] then
			clientConnections[instance][event]:Disconnect()
			clientConnections[instance][event] = nil
		end
	end,
}

function DebuggerEventBridge.new(fire)
	return setmetatable({
		_fire = fire,
		_storage = {},
		players = {},
	}, DebuggerEventBridge)
end

function DebuggerEventBridge:connect(instance, event, handler)
	if RunService:IsClient() then
		return instance[event]:Connect(handler)
	end

	-- Defensive: signals may belong to instances not yet parented to game
	-- (rare during plasma's draw — but if it happens, defer the connect
	-- until ancestry settles to avoid silently dropping the binding).
	if not game:IsAncestorOf(instance) then
		local connection
		connection = instance.AncestryChanged:Connect(function()
			if connection == nil then return end
			if game:IsAncestorOf(instance) then
				connection:Disconnect()
				connection = nil
				self:connect(instance, event, handler)
			end
		end)
		return
	end

	if self._storage[instance] == nil then
		self._storage[instance] = {}
		instance.Destroying:Connect(function()
			for ev in self._storage[instance] do
				self:_disconnect(instance, ev)
			end
		end)
	end
	self._storage[instance][event] = handler

	for _, player in self.players do
		self:_connectPlayerEvent(player, instance, event)
	end

	return {
		Disconnect = function()
			self:_disconnect(instance, event)
		end,
	}
end

function DebuggerEventBridge:_connectPlayerEvent(player, instance, event)
	-- Service instances aren't transmissible by reference; send the class
	-- name so the client resolves via game:GetService.
	if instance.Parent == game then instance = instance.ClassName end
	self._fire(player, "connect", instance, event)
end

function DebuggerEventBridge:_disconnectPlayerEvent(player, instance, event)
	if instance.Parent == game then instance = instance.ClassName end
	self._fire(player, "disconnect", instance, event)
end

function DebuggerEventBridge:connectPlayer(player)
	for instance, events in self._storage do
		for event in events do
			self:_connectPlayerEvent(player, instance, event)
		end
	end
	table.insert(self.players, player)
end

function DebuggerEventBridge:disconnectPlayer(player)
	local index = table.find(self.players, player)
	if not index then return end
	table.remove(self.players, index)
	for instance, events in self._storage do
		for event in events do
			self:_disconnectPlayerEvent(player, instance, event)
		end
	end
end

function DebuggerEventBridge:_disconnect(instance, event)
	if self._storage[instance] then
		self._storage[instance][event] = nil
	end
	for _, player in self.players do
		self:_disconnectPlayerEvent(player, instance, event)
	end
end

function DebuggerEventBridge:fireEventFromPlayer(player, instance, event, ...)
	if not table.find(self.players, player) then
		warn(player, "fired a debugger event but isn't connected to the server debugger")
		return
	end
	if type(instance) == "string" then instance = game:GetService(instance) end
	if not self._storage[instance] or not self._storage[instance][event] then
		return
	end
	self._storage[instance][event](...)
end

return DebuggerEventBridge
