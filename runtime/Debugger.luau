-- Public surface for the runtime ScreenGui debugger.
--
-- Usage (transpiled from C#):
--   Debugger.Run(Contexts.sharedInstance)   -- BEFORE constructing features
--   -- features built after this point self-register via DebuggerSingleton
--   if isClient then Debugger.Show() end
--
-- Public API is the dot-static surface at the bottom (Run / Show / Hide /
-- Toggle / IsServerView / SwitchToServerView / SwitchToClientView /
-- RegisterSystemName) — what the C# stub at stubs/Debug/Debugger.cs binds
-- to. Instance methods are underscore-prefixed and dispatched to from those.
--
-- Adapted from matter-ecs/matter lib/debugger/debugger.luau. Concepts
-- mapped: Matter.World → Context, Matter.Loop._worlds → contexts array,
-- Matter.Loop._orderedSystemsByEvent → feature._executeSystems, query
-- attribution → group iteration (DebuggerHookContext patches Group:GetEntities).

local RunService = game:GetService("RunService")
local ReplicatedStorage = game:GetService("ReplicatedStorage")
local Players = game:GetService("Players")
local UserInputService = game:GetService("UserInputService")

local UI_SIGNAL = RunService.Heartbeat

local Plasma = require(script.Parent.Plasma)
local DebuggerProfiler = require(script.Parent.DebuggerProfiler)
local DebuggerSnapshot = require(script.Parent.DebuggerSnapshot)
local SystemNameCache = require(script.Parent.SystemNameCache)
local DebuggerUi = require(script.Parent.DebuggerUi)
local DebuggerWire = require(script.Parent.DebuggerWire)
local DebuggerEventBridge = require(script.Parent.DebuggerEventBridge)
local ClientBindings = require(script.Parent.ClientBindings)
local MouseHighlight = require(script.Parent.MouseHighlight)
local DebuggerHookContext = require(script.Parent.DebuggerHookContext)
local DebuggerSingleton = require(script.Parent.DebuggerSingleton)

local IS_SERVER = RunService:IsServer()
local IS_CLIENT = RunService:IsClient()

local TOGGLE_KEY = Enum.KeyCode.F4

local Debugger = {}
Debugger.__index = Debugger

-- Internal ctor — `Debugger.Run` is the only public entry point.
local function _new(authorize)
	local wire = DebuggerWire.GetOrCreateRemoteEvent()
	local profiler = DebuggerProfiler.new()

	local self = setmetatable({
		plasma = Plasma,
		enabled = false,
		-- Live-gate predicate. nil → server "start" rejects everyone (existing
		-- safe default). Studio bypasses this entirely. Client-side install
		-- has already been gated upstream in Debugger.Run, so this field is
		-- only consulted on the server when "start" arrives.
		_authorize = authorize,

		contexts = {},          -- array of { context, lookup, listener }
		-- Single shared profiler across every Feature. featureGroups keeps
		-- the Sidebar/ErrorInspect widget shape stable (they walk
		-- featureGroups[i].features / .profiler) — there's just one entry now.
		_features = {},
		_profiler = profiler,
		_profilerBound = profiler:Bind(),
		featureGroups = {
			{ features = {}, profiler = profiler },
		},

		snapshot = DebuggerSnapshot.new(),

		debugContext = nil,     -- currently inspected context (ui state)
		debugEntity = nil,      -- currently inspected entity:creationIndex()
		debugSystem = nil,      -- currently inspected system instance
		hoverEntity = nil,      -- mouse-highlight target
		skipSystemsAcrossFeatures = {},  -- (system) → bool, lifted from profiler skip set

		_seenEvents = {},
		_eventBridge = DebuggerEventBridge.new(function(...) wire:FireClient(...) end),
		_playersUsingDebugger = {},
		_hydrated = false,
		_wire = wire,
		_toggleConnection = nil,
		_currentDebugSystemStack = {},  -- HookContext writes here for query attribution
	}, Debugger)

	-- featureGroups[1].features aliases _features so widget code sees every
	-- Feature that self-registers later.
	self.featureGroups[1].features = self._features

	if IS_SERVER then
		self:_connectRemoteEvent()
	elseif IS_CLIENT then
		ClientBindings.Connect(self)
	end

	return self
end

function Debugger:_installGui()
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

	-- Plasma frame driver. Always Heartbeat; if you need RenderStepped
	-- specifically, you can fork — but Heartbeat after physics is what
	-- typical Roblox UIs use and matches the rest of this plugin.
	self._uiConnection = UI_SIGNAL:Connect(function(deltaTime)
		self:_tick(deltaTime)
	end)

	if IS_CLIENT then
		self.plasma.hydrateAutomaticSize()
		self._hydrated = true

		self._toggleConnection = UserInputService.InputBegan:Connect(function(input, processed)
			if processed then return end
			if input.KeyCode == TOGGLE_KEY then self:_toggle() end
		end)
	end
end

-- Called by Feature.new at construction time when Debugger.Current is set.
-- Wires the shared profiler into the Feature's Systems dispatch and tracks
-- it for the UI sidebar. Lazy-built features hit this path the same way as
-- bootstrap-time features — no AutoInitialize / AddProfiledFeatures dance.
function Debugger:_attachFeatureProfiler(feature)
	feature:SetProfiler(self._profilerBound)
	table.insert(self._features, feature)
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
		-- MouseHighlight uses plasma.useState for hover-debounce, so it
		-- has to run inside the plasma frame, not after finishFrame.
		if IS_CLIENT then
			MouseHighlight(self, self._wire)
		end
	end)
	self.plasma.finishFrame(self.plasmaNode)

	self.snapshot:EndFrame()
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

function Debugger:_show()
	assert(IS_CLIENT, "Debugger.Show may only be called on the client")
	self:_enable()
	if self.guiParent then self.guiParent.Enabled = true end
end

function Debugger:_hide()
	assert(IS_CLIENT, "Debugger.Hide may only be called on the client")
	self:_disable()
	if self.guiParent then self.guiParent.Enabled = false end
	if self:_isServerView() then self:_switchToClientView() end
end

function Debugger:_toggle()
	if self.enabled then self:_hide() else self:_show() end
end

function Debugger:_isServerView()
	return self._serverGuiEnabled == true
end

function Debugger:_switchToServerView()
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

function Debugger:_switchToClientView()
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
			-- Production gate: outside Studio, require an authorize predicate
			-- and check it against the requesting player's UserId. Without
			-- one, every player is rejected (the safe default).
			if not RunService:IsStudio() then
				if self._authorize == nil or not self._authorize(player.UserId) then
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

-- ============================================================================
-- Dot-static public surface — what C# `public static extern` binds to.
-- ============================================================================

-- Entry point. Idempotent — second call returns the existing singleton.
-- Walks contexts.allContexts and reads `componentNames` off each context
-- (codegen-emitted on {Ctx}Context partial). After this returns,
-- DebuggerSingleton.Current is set, so every Feature constructed afterwards
-- self-attaches its profiler.
--
-- authorize (optional): (userId: number) -> bool. Gates install in live.
--   Studio:                    ignored — install always proceeds.
--   Client live, nil:          NO-OP. Current stays nil → Features don't
--                              attach a profiler, F4 binding never installs.
--   Client live, returns true: install proceeds for this player.
--   Client live, returns false: NO-OP. Same as nil.
--   Server live:               install always proceeds (the wire needs to
--                              listen); the predicate is stored and applied
--                              to the "start" remote per requesting player.
function Debugger.Run(contexts, authorize)
	if DebuggerSingleton.Current ~= nil then return DebuggerSingleton.Current end

	assert(contexts ~= nil, "Debugger.Run: contexts must not be nil — pass Contexts.sharedInstance")

	-- Client gate. Server unconditionally installs the wire listener — the
	-- per-player gate runs there at "start" time instead.
	if IS_CLIENT and not RunService:IsStudio() then
		if authorize == nil then return nil end
		local localPlayer = Players.LocalPlayer
		if localPlayer == nil or not authorize(localPlayer.UserId) then
			return nil
		end
	end

	local self = _new(authorize)
	DebuggerSingleton.Current = self

	local all = contexts:allContexts()
	assert(type(all) == "table", "Debugger.Run: contexts.allContexts() must return an array")
	for i = 1, #all do
		local ctx = all[i]
		local names = ctx.componentNames
		if type(names) == "function" then names = ctx:componentNames() end
		table.insert(self.contexts, { context = ctx, lookup = names })
	end

	self:_installGui()
	return self
end

function Debugger.RegisterSystemName(classTable, name)
	SystemNameCache.RegisterSystemName(classTable, name)
end

function Debugger.Show()
	local c = DebuggerSingleton.Current
	assert(c ~= nil, "Debugger.Show called before Debugger.Run")
	c:_show()
end

function Debugger.Hide()
	local c = DebuggerSingleton.Current
	if c ~= nil then c:_hide() end
end

function Debugger.Toggle()
	local c = DebuggerSingleton.Current
	if c ~= nil then c:_toggle() end
end

function Debugger.IsServerView()
	local c = DebuggerSingleton.Current
	return c ~= nil and c:_isServerView()
end

function Debugger.SwitchToServerView()
	local c = DebuggerSingleton.Current
	assert(c ~= nil, "Debugger.SwitchToServerView called before Debugger.Run")
	c:_switchToServerView()
end

function Debugger.SwitchToClientView()
	local c = DebuggerSingleton.Current
	if c ~= nil then c:_switchToClientView() end
end

return Debugger
