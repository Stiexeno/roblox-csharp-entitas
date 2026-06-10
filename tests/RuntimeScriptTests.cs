namespace Entities.Tests
{
	// Structural assertions over runtime/*.luau. No Luau VM in this test
	// harness, so we can't execute the modules — but the invariants we
	// care about (snapshot drain order, frame-buffer flush position) are
	// observable as source-level shapes, and regressions in those shapes
	// are exactly what would slip a multiplayer race past review.
	public class RuntimeScriptTests
	{
		private static string ReadRuntime(string fileName)
		{
			string path = Path.Combine(TestHarness.PluginRoot, "runtime", fileName);
			Assert.True(File.Exists(path), $"Expected runtime file {path}");
			return File.ReadAllText(path);
		}

		// ----------------------------------------------------------------
		// EntitiesReplication: late-join snapshot must drain AFTER the
		// per-frame buffer so a joining player can't observe a snapshot
		// built before this frame's mutations applied (or worse: a
		// snapshot ordered before the FireAllClients batch carrying the
		// same frame's ops).
		// ----------------------------------------------------------------

		[Fact]
		public void Replication_PendingSnapshots_FieldIsDeclared()
		{
			// Init-time field on the module table so heartbeatTick can
			// drain it without nil-checking on every frame.
			string src = ReadRuntime("EntitiesReplication.luau");
			Assert.Contains("EntitiesReplication._pendingSnapshots = {}", src);
		}

		[Fact]
		public void Replication_ReadyEvent_OnlyEnqueuesPlayer()
		{
			// Snapshot must NOT fire inline from OnServerEvent — that
			// caused the race we're closing. The handler now: (a) validates
			// the client's build digest against the registered expectation,
			// (b) on match, queues a (player, contextName) entry for the
			// heartbeat snapshot phase. No inline FireClient, no inline
			// snapshotter walk.
			string src = ReadRuntime("EntitiesReplication.luau");
			string handlerBody = ExtractBetween(src,
				"readyEv.OnServerEvent:Connect(function(player, contextName, clientDigest)",
				"end)");
			Assert.Contains("table.insert(EntitiesReplication._pendingSnapshots", handlerBody);
			Assert.Contains("player:Kick", handlerBody);
			Assert.DoesNotContain("FireClient", handlerBody);
			Assert.DoesNotContain("snapshotFn", handlerBody);
		}

		[Fact]
		public void Replication_Heartbeat_DrainsBufferBeforeSnapshots()
		{
			// Source-order matters here: the FireAllClients call for the
			// per-frame buffer must appear in heartbeatTick BEFORE the
			// FireClient call for the snapshot. Same RemoteEvent, same
			// tick — Roblox guarantees per-event ordering, so source
			// order is the ordering the client sees.
			string src = ReadRuntime("EntitiesReplication.luau");
			string body = ExtractBetween(src,
				"local function heartbeatTick()",
				"\nend");
			int fireAll = body.IndexOf("FireAllClients", System.StringComparison.Ordinal);
			int pendingDrain = body.IndexOf("_pendingSnapshots", System.StringComparison.Ordinal);
			int fireOne = body.IndexOf("FireClient(player", System.StringComparison.Ordinal);
			Assert.True(fireAll > 0, "heartbeatTick must FireAllClients for the per-frame buffer");
			Assert.True(pendingDrain > fireAll,
				"_pendingSnapshots must be drained AFTER the FireAllClients buffer flush");
			Assert.True(fireOne > pendingDrain,
				"FireClient(player, ...) for snapshots must come AFTER the pending-drain block opens");
		}

		[Fact]
		public void Replication_ReadyEvent_KicksOnDigestMismatch()
		{
			// Hard fail on digest mismatch — the only safe response when
			// componentIndex ↔ type mappings disagree across the wire.
			// Comparison is "expected != nil and clientDigest != expected"
			// so an unregistered context (no expectation yet) still passes;
			// the codegen orders RegisterDigest before RegisterSnapshotter
			// to keep that window closed.
			string src = ReadRuntime("EntitiesReplication.luau");
			string handlerBody = ExtractBetween(src,
				"readyEv.OnServerEvent:Connect(function(player, contextName, clientDigest)",
				"end)");
			Assert.Contains("expected = EntitiesReplication._digests[contextName]", handlerBody);
			Assert.Contains("player:Kick(", handlerBody);
			Assert.Contains("clientDigest ~= expected", handlerBody);
		}

		[Fact]
		public void Replication_Subscribe_FiresReadyWithDigest()
		{
			// Client-side handshake: first Subscribe per context fires the
			// Ready event with (contextName, digest). Server reads both.
			string src = ReadRuntime("EntitiesReplication.luau");
			string subscribeBody = ExtractBetween(src,
				"function EntitiesReplication.Subscribe(contextName, buildDigest, handler)",
				"\nend");
			Assert.Contains("readyEv:FireServer(contextName, buildDigest)", subscribeBody);
		}

		[Fact]
		public void Replication_Heartbeat_ResetsPendingSnapshotsBeforeFiring()
		{
			// Reset-before-iterate so a Ready that arrives mid-iteration
			// (from a player joining during another's snapshot send)
			// lands in the next frame's queue, not this one — keeps the
			// per-frame ordering invariant intact.
			string src = ReadRuntime("EntitiesReplication.luau");
			string body = ExtractBetween(src,
				"local pending = EntitiesReplication._pendingSnapshots",
				"\nend");
			int reset = body.IndexOf("EntitiesReplication._pendingSnapshots = {}", System.StringComparison.Ordinal);
			int loop = body.IndexOf("for i = 1, #pending do", System.StringComparison.Ordinal);
			Assert.True(reset > 0 && loop > 0, "expected snapshot reset and snapshot loop");
			Assert.True(reset < loop, "_pendingSnapshots must be zeroed before iterating");
		}

		// ----------------------------------------------------------------
		// Delta encoding — QueueSet diffs new fields against the per-
		// (context, entity, component) baseline, ships only changed
		// positions tagged with a bitmask. QueueRemove clears the
		// baseline so a future re-add starts fresh.
		// ----------------------------------------------------------------

		[Fact]
		public void Delta_BaselinesTableIsDeclared()
		{
			string src = ReadRuntime("EntitiesReplication.luau");
			Assert.Contains("EntitiesReplication._baselines = {}", src);
		}

		[Fact]
		public void Delta_QueueSet_FirstSendEmitsAllOnesBitmask()
		{
			// `prior == nil` branch: every field is "changed" because
			// there's no baseline yet, so bitmask gets every bit set and
			// every field flows into the delta tail.
			string src = ReadRuntime("EntitiesReplication.luau");
			string body = ExtractBetween(src,
				"function EntitiesReplication.QueueSet(contextName, componentIndex, entityId, ...)",
				"\nend");
			Assert.Contains("if prior == nil then", body);
			Assert.Contains("bit32.bor(bitmask, bit32.lshift(1, i - 1))", body);
		}

		[Fact]
		public void Delta_QueueSet_IdempotentSkip_WhenBitmaskIsZero()
		{
			// All fields equal baseline → bitmask stays 0 → skip the
			// enqueue entirely. Saves bandwidth on reactive systems that
			// re-fire Replace{X} with unchanged values.
			string src = ReadRuntime("EntitiesReplication.luau");
			string body = ExtractBetween(src,
				"function EntitiesReplication.QueueSet(contextName, componentIndex, entityId, ...)",
				"\nend");
			Assert.Contains("if bitmask == 0 then return end", body);
		}

		[Fact]
		public void Delta_QueueSet_FlagShortCircuits()
		{
			// Flag (fieldCount == 0) bypasses the bitmask path entirely —
			// prior-nil emits the op, prior-present skips. Wire shape is
			// {0, ci, eid, 0} (bitmask = 0).
			string src = ReadRuntime("EntitiesReplication.luau");
			string body = ExtractBetween(src,
				"function EntitiesReplication.QueueSet(contextName, componentIndex, entityId, ...)",
				"\nend");
			Assert.Contains("if fieldCount == 0 then", body);
			Assert.Contains("{ 0, componentIndex, entityId, 0 }", body);
		}

		[Fact]
		public void Delta_QueueSet_UpdatesBaselineAfterEmit()
		{
			// Baseline update must happen *after* the diff (we still need
			// to compare against the prior value) and *before* the
			// table.insert (so a callback firing on the same frame sees
			// the new baseline). Order matters here.
			string src = ReadRuntime("EntitiesReplication.luau");
			string body = ExtractBetween(src,
				"function EntitiesReplication.QueueSet(contextName, componentIndex, entityId, ...)",
				"\nend");
			int baselineWrite = body.IndexOf("entBase[componentIndex] = newFields", System.StringComparison.Ordinal);
			int tableInsert = body.LastIndexOf("table.insert(bufferFor(contextName), op)", System.StringComparison.Ordinal);
			Assert.True(baselineWrite > 0 && tableInsert > 0);
			Assert.True(baselineWrite < tableInsert,
				"Baseline must be updated before the op is pushed.");
		}

		[Fact]
		public void Delta_QueueRemove_ClearsBaselineEntry()
		{
			// Remove drops the per-component baseline so a future re-add
			// of the same component on the same entity starts fresh
			// (first-send branch → all-ones bitmask).
			string src = ReadRuntime("EntitiesReplication.luau");
			string body = ExtractBetween(src,
				"function EntitiesReplication.QueueRemove(contextName, componentIndex, entityId)",
				"\nend");
			Assert.Contains("entBase[componentIndex] = nil", body);
		}

		[Fact]
		public void Delta_QueueRemove_CollapsesEmptyEntityBaseline()
		{
			// Long-running games accumulate stale entityId keys for fully
			// torn-down entities unless we collapse the entity-level
			// table once it empties.
			string src = ReadRuntime("EntitiesReplication.luau");
			string body = ExtractBetween(src,
				"function EntitiesReplication.QueueRemove(contextName, componentIndex, entityId)",
				"\nend");
			Assert.Contains("if next(entBase) == nil then", body);
			Assert.Contains("ctxBase[entityId] = nil", body);
		}

		private static string ExtractBetween(string src, string startToken, string endToken)
		{
			int start = src.IndexOf(startToken, System.StringComparison.Ordinal);
			Assert.True(start >= 0, $"expected to find: {startToken}");
			int end = src.IndexOf(endToken, start + startToken.Length, System.StringComparison.Ordinal);
			Assert.True(end >= 0, $"expected to find: {endToken} after {startToken}");
			return src.Substring(start, end - start);
		}
	}
}
