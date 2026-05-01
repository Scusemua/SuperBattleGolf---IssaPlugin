using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using IssaPlugin.Items;
using IssaPlugin.Network;
using IssaPlugin.Patches;
using Mirror;
using UnityEngine;

namespace IssaPlugin
{
    /// <summary>
    /// Runs on the host only. Every SyncInterval seconds it:
    /// 1. Clears EffectiveWeights and rebuilds all ItemSpawnerSettings pools so
    ///    the server's item pools reflect any config changes made in-game.
    /// 2. Broadcasts current per-pool spawn weights to all clients so they can
    ///    update their local _serverPoolWeights before the next level load.
    /// </summary>
    public class SpawnWeightsSyncer : MonoBehaviour
    {
        private const float SyncInterval = 5f;

        // Sentinel: null ItemPoolWeights forces a sync on the first tick.
        private static SpawnWeightsMessage _lastSent = new SpawnWeightsMessage
        {
            ItemPoolWeights = null,
        };
        private static int _lastEnabledHash;

        // Re-applying base-game spawnChanceWeights after ResetRuntimeData().
        // ResetRuntimeData() rebuilds runtime pools from ScriptableObject defaults,
        // which resets any user-configured weights (e.g. 0%) back to their original
        // values. ServerUpdateSpawnChanceValue pushes each spawnChanceWeights entry
        // back into the runtime pool, restoring the user's configuration.
        private static readonly MethodInfo ServerUpdateSpawnChanceValueMethod = AccessTools.Method(
            typeof(MatchSetupRules),
            "ServerUpdateSpawnChanceValue"
        );
        private static readonly FieldInfo SpawnChanceWeightsField = AccessTools.Field(
            typeof(MatchSetupRules),
            "spawnChanceWeights"
        );

        private IEnumerator Start()
        {
            while (true)
            {
                yield return new WaitForSeconds(SyncInterval);
                if (NetworkServer.active)
                    BroadcastWeightsIfChanged();
            }
        }

        /// <summary>
        /// Forces an immediate pool rebuild and weight broadcast regardless of
        /// whether weights have changed. Call after any change that affects
        /// Enabled state without touching pool weight values (e.g. vote results).
        /// VoteManager calls this and continues to work correctly after this refactor.
        /// </summary>
        internal static void ForceServerSync()
        {
            _lastSent = new SpawnWeightsMessage { ItemPoolWeights = null };
            if (NetworkServer.active)
                BroadcastWeightsIfChanged();
        }

        /// <summary>
        /// Rebuilds server item pools and sends the current spawn weights to a
        /// single newly-joined client only. Does NOT broadcast to all clients —
        /// that avoids triggering Mirror's disconnect logic for any other client
        /// whose Steam transport connection happens to be momentarily stale.
        /// The _lastSent sentinel is reset so the next periodic tick will
        /// broadcast the authoritative state to all clients within 5 seconds.
        /// </summary>
        internal static void SyncToConnection(NetworkConnectionToClient conn)
        {
            if (!NetworkServer.active || !NetworkClient.active)
                return;

            foreach (var item in ItemRegistry.AllItems)
                item.ResetServerWeights();

            var poolWeights = new Dictionary<int, float[]>(ItemRegistry.AllItems.Count);
            foreach (var def in ItemRegistry.AllItems)
            {
                var weights = new float[6];
                for (int p = 0; p < 6; p++)
                    weights[p] = def.GetPoolWeight(p);
                poolWeights[(int)def.ItemType] = weights;
            }

            var msg = new SpawnWeightsMessage
            {
                CustomItemSpawnsEnabled = ModConfig.Global.CustomItemSpawnsEnabled.Value,
                ItemPoolWeights = poolWeights,
            };

            MatchSetupRulesPatches.EffectiveWeights.Clear();
            foreach (var settings in Resources.FindObjectsOfTypeAll<ItemSpawnerSettings>())
                settings.ResetRuntimeData();

            // Re-apply base-game spawnChanceWeights so user-configured 0% items (and
            // any other non-default weights) are not silently reset to defaults.
            ReapplyBaseGameWeights();

            // Force next periodic tick to SendToAll so the rest of the clients
            // also receive the freshly-rebuilt weights within 5 seconds.
            _lastSent = new SpawnWeightsMessage { ItemPoolWeights = null };

            conn.Send(msg);
            IssaPluginPlugin.Log.LogDebug(
                $"[SpawnWeights] Synced to joining client conn={conn.connectionId}."
            );
        }

        private static void BroadcastWeightsIfChanged()
        {
            // Writers are registered in OnStartClient. Mirror clears them on disconnect, so
            // don't attempt a send in the window between OnStopClient and the next OnStartClient.
            if (!NetworkClient.active)
                return;

            // Reset server overrides so the host resolves fresh from config.
            foreach (var item in ItemRegistry.AllItems)
                item.ResetServerWeights();

            // Build the per-pool weight snapshot.
            var poolWeights = new Dictionary<int, float[]>(ItemRegistry.AllItems.Count);
            foreach (var def in ItemRegistry.AllItems)
            {
                var weights = new float[6];
                for (int p = 0; p < 6; p++)
                    weights[p] = def.GetPoolWeight(p);
                poolWeights[(int)def.ItemType] = weights;
            }

            var msg = new SpawnWeightsMessage
            {
                CustomItemSpawnsEnabled = ModConfig.Global.CustomItemSpawnsEnabled.Value,
                ItemPoolWeights = poolWeights,
            };

            int enabledHash = 0;
            foreach (var def in ItemRegistry.AllItems)
                if (def.Enabled)
                    enabledHash ^= (int)def.ItemType;

            if (!ShouldBroadcast(msg, enabledHash))
                return;

            _lastSent = msg;
            _lastEnabledHash = enabledHash;

            // Clear stale EffectiveWeights before the full rebuild cycle so that
            // entries from disabled items do not persist.
            MatchSetupRulesPatches.EffectiveWeights.Clear();

            foreach (var settings in Resources.FindObjectsOfTypeAll<ItemSpawnerSettings>())
                settings.ResetRuntimeData();

            // ResetRuntimeData() rebuilds pools from ScriptableObject defaults, which
            // loses any user-configured base-game item weights (e.g. items set to 0%).
            // Re-apply spawnChanceWeights so those settings are honoured during gameplay.
            ReapplyBaseGameWeights();

            NetworkServer.SendToAll(msg);
            IssaPluginPlugin.Log.LogDebug($"[SpawnWeights] Synced: {msg}");
        }

        /// <summary>
        /// Re-applies MatchSetupRules.spawnChanceWeights to the runtime ItemPool instances
        /// after ResetRuntimeData() has rebuilt them from ScriptableObject defaults. Without
        /// this, base-game items set to 0% in the match setup UI will silently revert to
        /// their default spawn weights in the actual runtime pools.
        /// </summary>
        private static void ReapplyBaseGameWeights()
        {
            if (!NetworkServer.active)
                return;

            var rules = Object.FindAnyObjectByType<MatchSetupRules>();
            if (rules == null)
                return;

            var weights =
                SpawnChanceWeightsField.GetValue(rules)
                as IDictionary<MatchSetupRules.ItemPoolId, float>;
            if (weights == null)
                return;

            foreach (var key in weights.Keys)
            {
                if (ItemRegistry.IsCustomItem(key.itemType))
                    continue;
                ServerUpdateSpawnChanceValueMethod.Invoke(rules, new object[] { key });
            }
        }

        private static bool ShouldBroadcast(SpawnWeightsMessage newMsg, int enabledHash)
        {
            if (newMsg.ItemPoolWeights == null || _lastSent.ItemPoolWeights == null)
                return true;
            if (enabledHash != _lastEnabledHash)
                return true;
            if (newMsg.CustomItemSpawnsEnabled != _lastSent.CustomItemSpawnsEnabled)
                return true;
            if (newMsg.ItemPoolWeights.Count != _lastSent.ItemPoolWeights.Count)
                return true;

            foreach (var (itemTypeId, weights) in newMsg.ItemPoolWeights)
            {
                if (!_lastSent.ItemPoolWeights.TryGetValue(itemTypeId, out var prevWeights))
                    return true;
                for (int p = 0; p < 6; p++)
                    if (weights[p] != prevWeights[p])
                        return true;
            }
            return false;
        }

        /// <summary>Called on each client when a SpawnWeightsMessage arrives.</summary>
        internal static void HandleSpawnWeights(SpawnWeightsMessage msg)
        {
            if (NetworkServer.active)
                return; // host is authoritative

            ModConfig.Global.CustomItemSpawnsEnabled.Value = msg.CustomItemSpawnsEnabled;

            foreach (var (itemTypeId, weights) in msg.ItemPoolWeights)
            {
                if (!ItemRegistry.CustomItemDefinitionMap.TryGetValue(itemTypeId, out var def))
                    continue;
                for (int p = 0; p < weights.Length && p < 6; p++)
                    def.SetServerPoolWeight(p, weights[p]);
            }

            IssaPluginPlugin.Log.LogDebug($"[SpawnWeights] Received from host: {msg}");
        }
    }
}
