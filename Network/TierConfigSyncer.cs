// TierConfigSyncer.cs
// Manages the authoritative SpawnConfigSnapshot on the host and keeps clients in sync.
//
// ── Lifecycle ─────────────────────────────────────────────────────────────────
//   • Added to the plugin GameObject in Plugin.cs (alongside SpawnWeightsSyncer).
//   • Registers Mirror message handlers on Start.
//   • When the host edits settings via SpawnConfigUI and calls ApplyAndBroadcast(),
//     this class writes the changes to Configuration (persisting them) and sends a
//     TierConfigMessage to all connected clients.
//   • Non-host clients apply the received snapshot to their local
//     CustomItemDefinition fields so their UI shows the host's settings.
//
// ── Thread safety ─────────────────────────────────────────────────────────────
//   Everything here runs on the Unity main thread (Mirror handlers are dispatched
//   there), so no locking is required.

using IssaPlugin.Items;
using IssaPlugin.Network;
using Mirror;
using UnityEngine;

namespace IssaPlugin
{
    public class TierConfigSyncer : MonoBehaviour
    {
        // ── Singleton ──────────────────────────────────────────────────────────
        public static TierConfigSyncer Instance { get; private set; }

        // ── Live snapshot ─────────────────────────────────────────────────────
        /// <summary>
        /// Current spawn-config state as known by this process.
        ///
        /// On the host this is always authoritative (built from Configuration or
        /// updated via the UI).  On non-host clients it is a copy of the last
        /// message received from the host; it is read-only (the UI runs in
        /// display-only mode for clients).
        /// </summary>
        public SpawnConfigSnapshot CurrentSnapshot { get; private set; }

        // ── Gating state (runtime, not persisted) ──────────────────────────────
        // Per-tier gating lives inside TierSettings on the snapshot, but we also
        // cache it here for fast lookup by ItemRegistryPatches.
        // (Distance/place gating is evaluated per-spawn during
        //  ItemSpawnerResetRuntimeDataPatch.InjectIntoPool when pools are rebuilt.)

        // ── Unity lifecycle ───────────────────────────────────────────────────

        private void Awake()
        {
            if (Instance != null)
            {
                Destroy(this);
                return;
            }
            Instance = this;
        }

        private void Start()
        {
            // Register message reader/writer with Mirror.
            // Mirror looks these up by type name, so they must be registered
            // before any connection is established.  Start() is early enough.
            NetworkClient.RegisterHandler<TierConfigMessage>(OnClientReceivedTierConfig, false);

            // ModConfig.Initialize() is called in Plugin.Awake() before AddComponent,
            // but guard anyway in case of ordering surprises.
            if (ModConfig.Global.NumTiers != null)
                CurrentSnapshot = SpawnConfigSnapshot.FromConfiguration();
            else
                IssaPluginPlugin.Log.LogWarning(
                    "[TierConfigSyncer] Configuration not ready at Start — snapshot deferred."
                );

            IssaPluginPlugin.Log.LogInfo("[TierConfigSyncer] Initialised.");
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;

            if (NetworkClient.active)
                NetworkClient.UnregisterHandler<TierConfigMessage>();
        }

        // ── Host API ──────────────────────────────────────────────────────────

        /// <summary>
        /// Called by <see cref="SpawnConfigUI"/> when the host clicks "Apply".
        /// Writes the edited snapshot back to Configuration, triggers a pool
        /// rebuild, and broadcasts the new settings to all clients.
        /// </summary>
        public void ApplyAndBroadcast(SpawnConfigSnapshot editedSnapshot)
        {
            if (!NetworkServer.active)
            {
                IssaPluginPlugin.Log.LogWarning(
                    "[TierConfigSyncer] ApplyAndBroadcast called on a non-host client — ignoring."
                );
                return;
            }

            CurrentSnapshot = editedSnapshot;

            // Persist to BepInEx config + trigger SpawnWeightsSyncer.
            editedSnapshot.ApplyToConfiguration();

            // Send the full snapshot to all clients (including the listen-server
            // host's own client connection — HandleSpawnWeights there is a no-op
            // because NetworkServer.active guards against double-application).
            var wireMsg = TierConfigMessageSerialization.FromSnapshot(editedSnapshot);
            NetworkServer.SendToAll(wireMsg);

            IssaPluginPlugin.Log.LogInfo(
                "[TierConfigSyncer] Broadcast TierConfigMessage to all clients."
            );
        }

        /// <summary>
        /// Force an immediate re-broadcast of the current snapshot.
        /// Useful after an external change (e.g. vote result) that modifies
        /// Configuration without going through the UI.
        /// </summary>
        public void ForceBroadcast()
        {
            if (!NetworkServer.active)
                return;
            CurrentSnapshot = SpawnConfigSnapshot.FromConfiguration();
            var wireMsg = TierConfigMessageSerialization.FromSnapshot(CurrentSnapshot);
            NetworkServer.SendToAll(wireMsg);
            IssaPluginPlugin.Log.LogInfo("[TierConfigSyncer] Force-broadcast TierConfigMessage.");
        }

        // ── Client handler ────────────────────────────────────────────────────

        internal void OnClientReceivedTierConfig(TierConfigMessage msg)
        {
            // The listen-server host is also a client, but we don't want to
            // overwrite the host's authoritative snapshot with its own broadcast.
            if (NetworkServer.active)
                return;

            var snap = TierConfigMessageSerialization.ToSnapshot(msg);
            CurrentSnapshot = snap;

            // Propagate global values to ModConfig so BuildCustomEntries reads the
            // host's settings rather than the client's local defaults.
            // (OnClientReceivedTierConfig already writes per-item Enabled via
            // ModConfig.SetItemEnabled; these three mirror that same pattern.)
            ModConfig.Global.CustomItemSpawnsEnabled.Value = snap.CustomItemSpawnsEnabled;
            ModConfig.Global.CustomItemSpawnRate.Value     = snap.GlobalSpawnRateMultiplier;
            ModConfig.Global.CatchupBoostFactor.Value      = snap.CatchupBoostFactor;

            // Apply per-item enable / weight-override to local CustomItemDefinition
            // so the client-side UI reflects the host's settings.
            foreach (var kv in snap.ItemOverrides)
            {
                if (ItemRegistry.CustomItemDefinitionMap.TryGetValue(kv.Key, out var def))
                {
                    // Enabled state — calls ModConfig.SetItemEnabled, which is fine
                    // on clients too (they just won't propagate it further).
                    def.Enabled = kv.Value.Enabled;

                    // SpawnWeight: mirror the host's effective weight so the client's
                    // UI weight-display stays accurate.  We use the same _serverWeight
                    // path that SpawnWeightsSyncer uses.
                    if (kv.Value.SpawnWeightOverrideEnabled)
                        def.SpawnWeight = kv.Value.SpawnWeightOverride;
                    // If no per-item override, the weight comes from the tier; the
                    // existing SpawnWeightsSyncer already handles that path.
                }
            }

            IssaPluginPlugin.Log.LogInfo("[TierConfigSyncer] Applied TierConfigMessage from host.");
        }

        // ── Gating helpers (called from ItemSpawnerResetRuntimeDataPatch) ──────

        /// <summary>
        /// Returns false if the tier's gating conditions prevent this tier from
        /// spawning for the given player context.
        ///
        /// <paramref name="distanceBehindLeader"/> — how far behind the leading
        /// player this player is (in world-space units). Pass 0 if unknown.
        ///
        /// <paramref name="currentPlace"/> — 1-based position in the current
        /// leaderboard.  Pass 1 if unknown (first place gets everything).
        /// </summary>
        public bool IsTierAllowedForPlayer(int tier, float distanceBehindLeader, int currentPlace)
        {
            if (CurrentSnapshot == null)
                return true;

            if (!CurrentSnapshot.TierSettings.TryGetValue(tier, out var ts))
                return true;

            if (!ts.TierEnabled)
                return false;

            // Distance gate: player must be at least MinDistanceBehindLeader behind.
            if (
                ts.MinDistanceBehindLeader > 0f
                && distanceBehindLeader < ts.MinDistanceBehindLeader
            )
                return false;

            // Place gate: player must be in MinPlaceTrigger-th place or worse.
            if (ts.MinPlaceTrigger > 0 && currentPlace < ts.MinPlaceTrigger)
                return false;

            return true;
        }
    }
}
