-- Public surface for the runtime ScreenGui debugger.
--
-- Usage (transpiled from C#):
--   local debugger = Debugger.new(Plasma)
--   debugger:AttachContext(Contexts.sharedInstance.game, GameComponentsLookup.componentNames)
--   debugger:AutoInitialize({
--       { signal = RunService.Heartbeat,     features = { gameFeature } },
--       { signal = RunService.RenderStepped, features = { viewFeature } },
--   })
--   if isClient then debugger:Show() end
--
-- Adapted from matter-ecs/matter lib/debugger/debugger.luau. Concepts
-- mapped: Matter.World → Context, Matter.Loop._worlds → contexts array,
-- Matter.Loop._orderedSystemsByEvent → feature._executeSystems, query
-- attribution → group iteration (DebuggerHookContext patches Group:GetEntities).

local RunService = game:GetService("RunService")
local ReplicatedStorage = game:GetService("ReplicatedStorage")
local Players = game:GetService("Players")
local UserInputService = game:GetService("UserInputService")

local DebuggerProfiler = require(script.Parent.DebuggerProfiler)
local DebuggerSnapshot = require(script.Parent.DebuggerSnapshot)
local SystemNameCache = require(script.Parent.SystemNameCache)
local DebuggerUi = require(script.Parent.DebuggerUi)
local DebuggerWire = require(script.Parent.DebuggerWire)
local DebuggerEventBridge = require(script.Parent.DebuggerEventBridge)
local ClientBindings = require(script.Parent.ClientBindings)
local MouseHighlight = require(script.Parent.MouseHighlight)
local DebuggerHookContext = require(script.Parent.DebuggerHookContext)

local IS_SERVER = RunService:IsServer()
local IS_CLIENT = RunService:IsClient()

local TOGGLE_KEY = Enum.KeyCode.F4

local Debugger = {}
Debugger.__index = Debugger

local function assertCompatiblePlasma(plasma)
	if plasma == nil or plasma.highlight == nil then
		error("Plasma passed to Debugger is missing 'highlight' — update vendored plasma.")
	end
end

function Debugger.new(plasma)
	assertCompatiblePlasma(plasma)

	local wire = DebuggerWire.GetOrCreateRemoteEvent()

	local self = setmetatable({
		plasma = plasma,
		enabled = false,

		contexts = {},          -- array of { context, lookup, listener }
		featureGroups = {},     -- array of { signal, features, profiler, connection }

		snapshot = DebuggerSnapshot.new(),

		debugContext = nil,     -- currently inspected context (ui state)
		debugEntity = nil,      -- currently inspected entity:creationIndex()
		debugSystem = nil,      -- currently inspected system instance
		hoverEntity = nil,      -- mouse-highlight target
		skipSystemsAcrossFeatures = {},  -- (system) → bool, lifted from union of profiler skip sets

		_seenEvents = {},
		_eventBridge = DebuggerEventBridge.new(function(...) wire:FireClient(...) end),
		_playersUsingDebugger = {},
		_hydrated = false,
		_wire = wire,
		_toggleConnection = nil,
		_currentDebugSystemStack = {},  -- HookContext writes here for query attribution
	}, Debugger)

	if IS_SERVER then
		self:_connectRemoteEvent()
	elseif IS_CLIENT then
		ClientBindings.Connect(self)
	end

	return self
end

-- componentsLookup is the codegen-emitted {Ctx}ComponentsLookup.componentNames
-- array (indexed by componentIndex). Required so the UI can label rows.
function Debugger:AttachContext(context, componentsLookup)
	table.insert(self.contexts, { context = context, lookup = componentsLookup })
end

function Debugger:RegisterSystemName(classTable, name)
	SystemNameCache.RegisterSystemName(classTable, name)
end

function Debugger:AutoInitialize(groups)
	assert(type(groups) == "table", "AutoInitialize expects an array of {signal, features} groups")

	local parent = Instance.new("ScreenGui")
	parent.Name = "EntitiesDebugger"
	parent.DisplayOrder = 2^31 - 1
	parent.ResetOnSpawn = false
	parent.IgnoreGuiInset = true
	parent.AutoLocalize = false

	if IS_CLIENT then
		parent.Parent = Players.LocalPlayer:WaitForChild("PlayerGui")
	else
		parent.Parent = ReplicatedStorage
		parent.Enabled = false  -- server-side gui is mirror-only; client toggles
	end

	self.plasmaNode = self.plasma.new(parent)
	self.guiParent = parent

	for _, group in ipairs(groups) do
		local profiler = DebuggerProfiler.new()
		local bound = profiler:Bind()
		for _, feature in ipairs(group.features) do
			feature:SetProfiler(bound)
		end
		table.insert(self.featureGroups, {
			signal = group.signal,
			features = group.features,
			profiler = profiler,
			connection = nil,
		})
	end

	-- Drive plasma frames from the FIRST signal. Other signals still
	-- profile (their features each have SetProfiler set), but UI redraws
	-- once per first-signal tick — typical: Heartbeat for game logic, the
	-- UI shows 60Hz timings.
	if #self.featureGroups > 0 then
		local first = self.featureGroups[1]
		first.connection = first.signal:Connect(function(deltaTime)
			self:_tick(deltaTime)
		end)
	end

	if IS_CLIENT then
		self.plasma.hydrateAutomaticSize()
		self._hydrated = true

		self._toggleConnection = UserInputService.InputBegan:Connect(function(input, processed)
			if processed then return end
			if input.KeyCode == TOGGLE_KEY then self:Toggle() end
		end)
	end
end

function Debugger:_tick(deltaTime)
	if not self.enabled then return end

	self.snapshot:BeginFrame(os.clock())

	-- Full-mode snapshot: walk every attached context once per tick.
	-- Delta mode is reactive via Context._debugListeners (wired in _enable).
	if self.snapshot:GetMode() == "full" then
		for _, c in ipairs(self.contexts) do
			local entities = c.context:GetEntities()
			for i = 1, #entities do
				self.snapshot:RecordEntity(entities[i])
			end
		end
	end

	self.plasma.beginFrame(self.plasmaNode, function()
		self.plasma.setEventCallback(function(...)
			return self._eventBridge:connect(...)
		end)
		DebuggerUi(self)
	end)
	self.plasma.finishFrame(self.plasmaNode)

	self.snapshot:EndFrame()

	if IS_CLIENT then
		MouseHighlight(self, self._wire)
	end
end

function Debugger:_enable()
	if self.enabled then return end
	self.enabled = true

	DebuggerHookContext.Hook(self)

	-- Delta-mode capture: every component change hits the snapshot if mode
	-- is "delta". The listener is cheap (one nil-mode check per change)
	-- when the snapshot is off.
	for _, c in ipairs(self.contexts) do
		if c.listener == nil then
			local listener = {
				onComponentChanged = function(entity, _index, _component)
					if self.snapshot:GetMode() == "delta" then
						self.snapshot:RecordEntity(entity)
					end
				end,
			}
			c.context:AddDebugListener(listener)
			c.listener = listener
		end
	end
end

function Debugger:_disable()
	if not self.enabled then return end
	self.enabled = false
	DebuggerHookContext.Unhook()
	-- Listeners stay attached (cheap when snapshot is off and listener
	-- removal would require a Context API we haven't added). If Show()
	-- gets called again, the same listeners resume capturing.
end

function Debugger:Show()
	assert(IS_CLIENT, "Debugger:Show may only be called on the client")
	self:_enable()
	if self.guiParent then self.guiParent.Enabled = true end
end

function Debugger:Hide()
	assert(IS_CLIENT, "Debugger:Hide may only be called on the client")
	self:_disable()
	if self.guiParent then self.guiParent.Enabled = false end
	if self:IsServerView() then self:SwitchToClientView() end
end

function Debugger:Toggle()
	if self.enabled then self:Hide() else self:Show() end
end

function Debugger:IsServerView()
	return self._serverGuiEnabled == true
end

function Debugger:SwitchToServerView()
	assert(IS_CLIENT, "SwitchToServerView is client-only")
	self.debugSystem = nil
	if self._serverGui == nil then
		local found = ReplicatedStorage:WaitForChild("EntitiesDebugger", 5)
		if found ~= nil then
			found.Parent = Players.LocalPlayer:WaitForChild("PlayerGui")
			self._serverGui = found
		end
	end
	self._wire:FireServer("start")
	if self._serverGui ~= nil then self._serverGui.Enabled = true end
	self._serverGuiEnabled = true
end

function Debugger:SwitchToClientView()
	assert(IS_CLIENT, "SwitchToClientView is client-only")
	self._wire:FireServer("stop")
	if self._serverGui ~= nil then self._serverGui.Enabled = false end
	self._serverGuiEnabled = false
end

function Debugger:_connectRemoteEvent()
	self._wire.OnServerEvent:Connect(function(player, action, ...)
		if action == "event" then
			self._eventBridge:fireEventFromPlayer(player, ...)
		elseif action == "start" then
			-- Production gate: outside Studio, require an authorize hook.
			if not RunService:IsStudio() then
				if self.authorize == nil or not self.authorize(player) then
					return
				end
			end
			self:_connectPlayer(player)
		elseif action == "stop" then
			self:_disconnectPlayer(player)
		elseif action == "inspect" then
			self.debugEntity = (...)
		elseif action == "hover" then
			self.hoverEntity = (...)
		end
	end)
end

function Debugger:_connectPlayer(player)
	if not self.enabled then self:_enable() end
	self._eventBridge:connectPlayer(player)
	self._playersUsingDebugger[player] = true
end

function Debugger:_disconnectPlayer(player)
	self._eventBridge:disconnectPlayer(player)
	self._playersUsingDebugger[player] = nil
	if next(self._playersUsingDebugger) == nil then
		self:_disable()
	end
end

return Debugger
