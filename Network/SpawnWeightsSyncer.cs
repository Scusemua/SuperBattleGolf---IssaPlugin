using System.Collections;
using System.Collections.Generic;
using IssaPlugin.Items;
using IssaPlugin.Network;
using Mirror;
using UnityEngine;

namespace IssaPlugin
{
    /// Runs on the host only. Every <see cref="SyncInterval"/> seconds it:
    /// 1. Calls ResetRuntimeData() on every loaded ItemSpawnerSettings so the
    ///    server's item pools pick up any config changes made in-game.
    /// 2. Broadcasts the current spawn weights to all clients so they can update
    ///    their local config values before the next scene reload.
    public class SpawnWeightsSyncer : MonoBehaviour
    {
        private static Dictionary<int, float> _cachedItemSpawnWeights;
        private const float SyncInterval = 5f;

        // Sentinel: impossible weight value so the first check always triggers a sync.
        private static SpawnWeightsMessage _lastSent = new SpawnWeightsMessage
        {
            ItemSpawnWeights = null,
        };

        private static int _lastEnabledHash = 0;

        private IEnumerator Start()
        {
            while (true)
            {
                yield return new WaitForSeconds(SyncInterval);
                if (NetworkServer.active)
                    BroadcastWeightsIfChanged();
            }
        }

        /// Forces an immediate server pool rebuild and weight broadcast regardless of whether
        /// weights have changed. Call this after any change that affects Enabled state without
        /// touching SpawnWeight values (e.g., after a vote result is applied).
        internal static void ForceServerSync()
        {
            _lastSent = new SpawnWeightsMessage { ItemSpawnWeights = null };
            if (NetworkServer.active)
                BroadcastWeightsIfChanged();
        }

        private static void BroadcastWeightsIfChanged()
        {
            // Writers are registered in OnStartClient. Mirror clears them on disconnect, so
            // don't attempt a send in the window between OnStopClient and the next OnStartClient.
            if (!NetworkClient.active)
                return;

            // Clear any cached server weights so the host always resolves fresh from config,
            // even if this process previously ran as a client in the same Unity session.
            foreach (var item in ItemRegistry.AllItems)
                item.ResetServerWeight();

            if (_cachedItemSpawnWeights == null)
            {
                _cachedItemSpawnWeights = new Dictionary<int, float>();
            }
            else
            {
                _cachedItemSpawnWeights.Clear();
            }

            foreach (CustomItemDefinition item in ItemRegistry.AllItems)
            {
                _cachedItemSpawnWeights.Add((int)item.ItemType, item.SpawnWeight);
            }

            var msg = new SpawnWeightsMessage
            {
                CustomItemSpawnsEnabled = Configuration.CustomItemSpawnsEnabled.Value,
                ItemSpawnWeights = _cachedItemSpawnWeights,
            };

            // Compute a simple enabled-state fingerprint
            int enabledHash = 0;
            foreach (var item in ItemRegistry.AllItems)
                if (item.Enabled)
                    enabledHash ^= (int)item.ItemType;

            if (!ShouldUpdateWeights(msg, enabledHash))
                return;

            _lastSent = msg;
            _lastEnabledHash = enabledHash;

            // Re-inject custom weights into the live server pools.
            foreach (var settings in Resources.FindObjectsOfTypeAll<ItemSpawnerSettings>())
                settings.ResetRuntimeData();

            NetworkServer.SendToAll(msg);

            IssaPluginPlugin.Log.LogDebug($"[SpawnWeights] Synced: {msg.ToString()}");
        }

        private static bool ShouldUpdateWeights(SpawnWeightsMessage newMsg, int enabledHash)
        {
            // If one or both have null spawn weights, then return true.
            if (newMsg.ItemSpawnWeights == null || _lastSent.ItemSpawnWeights == null)
                return true;

            // The 'enabled' status of at least one item has changed.
            if (enabledHash != _lastEnabledHash)
                return true;

            if (newMsg.ItemSpawnWeights.Count != _lastSent.ItemSpawnWeights.Count)
            {
                IssaPluginPlugin.Log.LogWarning(
                    $"SpawnWeightsMessages have unequal number of entries: {newMsg.ItemSpawnWeights.Count} vs. {_lastSent.ItemSpawnWeights.Count}"
                );
                return true; // Probably shouldn't happen, but let's update to correct.
            }

            if (newMsg.CustomItemSpawnsEnabled != _lastSent.CustomItemSpawnsEnabled)
                return true;

            foreach (var (itemType, spawnWeight) in newMsg.ItemSpawnWeights)
            {
                if (!_lastSent.ItemSpawnWeights.ContainsKey(itemType))
                {
                    // Doesn't have an entry... shouldn't happen, but if it does, they should be updated.
                    IssaPluginPlugin.Log.LogError(
                        $"SpawnWeightsMessage is missing entry for item type ${itemType}"
                    );
                    return true;
                }

                if (spawnWeight != _lastSent.ItemSpawnWeights[itemType])
                {
                    return true; // Mismatched spawn weight; need to update.
                }
            }

            return false; // They're equal. Don't need to update weights.
        }

        /// Called on each client when a SpawnWeightsMessage arrives from the host.
        internal static void HandleSpawnWeights(SpawnWeightsMessage msg)
        {
            // Skip on the listen-server host; it already has the correct values.
            if (NetworkServer.active)
                return;

            foreach (var (itemType, spawnWeight) in msg.ItemSpawnWeights)
            {
                if (ItemRegistry.CustomItemDefinitionMap.TryGetValue(itemType, out var itemDef))
                    itemDef.SpawnWeight = spawnWeight;
            }

            IssaPluginPlugin.Log.LogDebug($"[SpawnWeights] Received from host: {msg.ToString()}");
        }
    }
}
