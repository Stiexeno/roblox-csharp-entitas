namespace Entities.Tests
{
	// Shape tests for the runtime ScreenGui debugger added in the
	// woolly-singing-trinket plan. These don't execute Lua (the test
	// harness has no Luau VM) — they verify that the modifications to
	// existing runtime files stay in place and that the new debugger
	// files contain the API surfaces the plan committed to. Catches
	// accidental deletions and locks the file layout.
	//
	// Behavior tests (entity-listener firing order, snapshot ring
	// wrap-around with creationIndex stability) require an in-Studio
	// manual run — see the plan's Verification section.
	public class DebuggerTests
	{
		private static string RuntimeFile(params string[] segments)
		{
			string[] all = new string[segments.Length + 2];
			all[0] = TestHarness.PluginRoot;
			all[1] = "runtime";
			System.Array.Copy(segments, 0, all, 2, segments.Length);
			return System.IO.Path.Combine(all);
		}

		private static string Read(string path)
		{
			Assert.True(System.IO.File.Exists(path), $"Expected file {path}");
			return System.IO.File.ReadAllText(path);
		}

		// ----------------------------------------------------------------
		// Runtime hooks added to existing files.
		// ----------------------------------------------------------------

		[Fact]
		public void Systems_HasProfilerHook()
		{
			string s = Read(RuntimeFile("Systems.luau"));
			Assert.Contains("self._profiler = nil", s);
			Assert.Contains("function Systems:SetProfiler(fn)", s);
			// Each phase loop must have both the no-profiler and the
			// profiler branches — the no-debugger path stays direct.
			foreach (string phase in new[] { "Initialize", "Execute", "Cleanup", "TearDown" })
			{
				Assert.Contains($"function Systems:{phase}()", s);
				Assert.Contains($"profiler(list[i], \"{phase}\")", s);
			}
		}

		[Fact]
		public void Context_HasDebugListenerHook()
		{
			string s = Read(RuntimeFile("Context.luau"));
			Assert.Contains("self._debugListeners = nil", s);
			Assert.Contains("function Context:AddDebugListener(listener)", s);
			// onCreated fires from Initialize, onDestroyed from
			// _OnEntityDestroyed, onComponentChanged from
			// NotifyComponentChanged. Verify each call site fires.
			Assert.Contains("l.onCreated(entity)", s);
			Assert.Contains("l.onDestroyed(entity)", s);
			Assert.Contains("l.onComponentChanged(entity, index, component)", s);
		}

		// ----------------------------------------------------------------
		// Plasma vendored under runtime/Plasma/. Top-level + widgets.
		// ----------------------------------------------------------------

		[Fact]
		public void Plasma_VendoredTopLevelFilesExist()
		{
			foreach (string name in new[] {
				"init.luau", "Runtime.luau", "Style.luau",
				"automaticSize.luau", "create.luau", "createConnect.luau",
				"hydrateAutomaticSize.luau",
			})
			{
				string path = RuntimeFile("Plasma", name);
				Assert.True(System.IO.File.Exists(path), $"Plasma file missing: {path}");
			}
		}

		[Fact]
		public void Plasma_VendoredWidgetsExist()
		{
			foreach (string name in new[] {
				"arrow.luau", "blur.luau", "button.luau", "checkbox.luau",
				"error.luau", "heading.luau", "highlight.luau", "label.luau",
				"portal.luau", "row.luau", "slider.luau", "space.luau",
				"spinner.luau", "table.luau", "window.luau",
			})
			{
				string path = RuntimeFile("Plasma", "widgets", name);
				Assert.True(System.IO.File.Exists(path), $"Plasma widget missing: {path}");
			}
		}

		[Fact]
		public void Plasma_InitReExportsExpectedApi()
		{
			string init = Read(RuntimeFile("Plasma", "init.luau"));
			// Spot-check the surface our debugger actually uses; if any of
			// these are gone, the debugger is broken.
			foreach (string surface in new[] {
				"new", "beginFrame", "finishFrame", "useState", "useInstance",
				"useKey", "setEventCallback", "window", "button", "checkbox",
				"row", "label", "heading", "highlight",
			})
			{
				Assert.Contains(surface + " =", init);
			}
		}

		// ----------------------------------------------------------------
		// Debugger files exist and export expected surfaces.
		// ----------------------------------------------------------------

		[Theory]
		[InlineData("Debugger.luau")]
		[InlineData("DebuggerUi.luau")]
		[InlineData("DebuggerProfiler.luau")]
		[InlineData("DebuggerSnapshot.luau")]
		[InlineData("DebuggerWire.luau")]
		[InlineData("DebuggerEventBridge.luau")]
		[InlineData("DebuggerHookContext.luau")]
		[InlineData("ClientBindings.luau")]
		[InlineData("MouseHighlight.luau")]
		[InlineData("RollingAverage.luau")]
		[InlineData("FormatTable.luau")]
		[InlineData("SystemNameCache.luau")]
		public void Debugger_FileExists(string name)
		{
			string path = RuntimeFile("Debug", name);
			Assert.True(System.IO.File.Exists(path), $"Debug file missing: {path}");
		}

		[Theory]
		[InlineData("Container.luau")]
		[InlineData("Panel.luau")]
		[InlineData("SelectionList.luau")]
		[InlineData("ContextInspect.luau")]
		[InlineData("EntityInspect.luau")]
		[InlineData("ValueInspect.luau")]
		[InlineData("GroupInspect.luau")]
		[InlineData("HooksInspect.luau")]
		[InlineData("QueryInspect.luau")]
		[InlineData("ErrorInspect.luau")]
		[InlineData("RealmSwitch.luau")]
		[InlineData("Tooltip.luau")]
		[InlineData("HoverInspect.luau")]
		[InlineData("CodeText.luau")]
		public void Debugger_WidgetExists(string name)
		{
			string path = RuntimeFile("Debug", "widgets", name);
			Assert.True(System.IO.File.Exists(path), $"Widget missing: {path}");
		}

		[Fact]
		public void Debugger_PublicApiPresent()
		{
			string s = Read(RuntimeFile("Debug", "Debugger.luau"));
			foreach (string member in new[] {
				"function Debugger.new()",
				"function Debugger:AttachContext(context, componentsLookup)",
				"function Debugger:RegisterSystemName(classTable, name)",
				"function Debugger:AutoInitialize(signal, features)",
				"function Debugger:AddFeatureGroup(signal, features)",
				"function Debugger:Show()",
				"function Debugger:Hide()",
				"function Debugger:Toggle()",
				"function Debugger:IsServerView()",
				"function Debugger:SwitchToServerView()",
				"function Debugger:SwitchToClientView()",
			})
			{
				Assert.Contains(member, s);
			}
		}

		[Fact]
		public void Debugger_WiresUpRemoteEventActions()
		{
			string s = Read(RuntimeFile("Debug", "Debugger.luau"));
			// Each server-side action handler — these are the wire shape
			// the plan committed to; if they disappear, server view breaks.
			Assert.Contains("if action == \"event\" then", s);
			Assert.Contains("elseif action == \"start\" then", s);
			Assert.Contains("elseif action == \"stop\" then", s);
			Assert.Contains("elseif action == \"inspect\" then", s);
			Assert.Contains("elseif action == \"hover\" then", s);
		}

		[Fact]
		public void DebuggerSnapshot_HasThreeModes()
		{
			string s = Read(RuntimeFile("Debug", "DebuggerSnapshot.luau"));
			Assert.Contains("MODE_OFF = \"off\"", s);
			Assert.Contains("MODE_DELTA = \"delta\"", s);
			Assert.Contains("MODE_FULL = \"full\"", s);
			// creationIndex keying (the entity-pool aliasing risk).
			Assert.Contains("entity:creationIndex()", s);
		}

		[Fact]
		public void DebuggerProfiler_TracksSamplesAndSkip()
		{
			string s = Read(RuntimeFile("Debug", "DebuggerProfiler.luau"));
			Assert.Contains("self._samples", s);
			Assert.Contains("self._skip", s);
			Assert.Contains("self._errors", s);
			Assert.Contains("RollingAverage.addSample(samples, dt)", s);
			// skipSystems short-circuit must happen before the timing path.
			Assert.Contains("if self._skip[system] then return end", s);
		}

		[Fact]
		public void DebuggerWire_UsesPluginsEntitiesNamespace()
		{
			string s = Read(RuntimeFile("Debug", "DebuggerWire.luau"));
			Assert.Contains("WaitForChild(\"Plugins\")", s);
			Assert.Contains("WaitForChild(\"Entities\")", s);
			Assert.Contains("REMOTE_NAME = \"Debugger\"", s);
		}

		[Fact]
		public void HookContext_UsesCurrentSystemStack()
		{
			string s = Read(RuntimeFile("Debug", "DebuggerHookContext.luau"));
			Assert.Contains("PushDebugSystem", s);
			Assert.Contains("PopDebugSystem", s);
			Assert.Contains("function DebuggerHookContext.Hook(debugger)", s);
			Assert.Contains("function DebuggerHookContext.Unhook()", s);
			// Must save the original Group.GetEntities for Unhook to restore.
			Assert.Contains("originalGetEntities = Group.GetEntities", s);
		}

		[Fact]
		public void MouseHighlight_ReadsNamespacedAttributes()
		{
			string s = Read(RuntimeFile("Debug", "MouseHighlight.luau"));
			Assert.Contains("debug_serverEntityId", s);
			Assert.Contains("debug_clientEntityId", s);
			Assert.Contains("Enum.KeyCode.LeftAlt", s);
		}

		[Fact]
		public void SystemNameCache_HasRegistryOverride()
		{
			string s = Read(RuntimeFile("Debug", "SystemNameCache.luau"));
			Assert.Contains("function SystemNameCache.GetName(system)", s);
			Assert.Contains("function SystemNameCache.RegisterSystemName(classTable, name)", s);
			// debug.info-based resolution is the no-transpiler-changes path
			// the plan committed to.
			Assert.Contains("debug.info(phaseFn, \"s\")", s);
		}
	}
}
