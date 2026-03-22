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
        private const float SyncInterval = 5f;

        // Sentinel: impossible weight value so the first check always triggers a sync.
        private static SpawnWeightsMessage _lastSent = new SpawnWeightsMessage
        {
            ItemSpawnWeights = null,
        };

        private IEnumerator Start()
        {
            while (true)
            {
                yield return new WaitForSeconds(SyncInterval);
                if (NetworkServer.active)
                    BroadcastWeightsIfChanged();
            }
        }

        private static void BroadcastWeightsIfChanged()
        {
            Dictionary<int, float> itemSpawnWeights = new Dictionary<int, float>();
            foreach (CustomItemDefinition item in ItemRegistry.AllItems)
            {
                itemSpawnWeights.Add((int)item.ItemType, item.SpawnWeight);
            }

            var msg = new SpawnWeightsMessage
            {
                CustomItemSpawnsEnabled = Configuration.CustomItemSpawnsEnabled.Value,
                ItemSpawnWeights = itemSpawnWeights,
            };

            if (!ShouldUpdateWeights(msg, _lastSent))
                return;

            _lastSent = msg;

            // Re-inject custom weights into the live server pools.
            foreach (var settings in Resources.FindObjectsOfTypeAll<ItemSpawnerSettings>())
                settings.ResetRuntimeData();

            NetworkServer.SendToAll(msg);

            IssaPluginPlugin.Log.LogDebug($"[SpawnWeights] Synced: {msg.ToString()}");
        }

        private static bool ShouldUpdateWeights(SpawnWeightsMessage a, SpawnWeightsMessage b)
        {
            // If one or both have null spawn weights, then return true.
            if (a.ItemSpawnWeights == null || b.ItemSpawnWeights == null)
                return true;

            if (a.ItemSpawnWeights.Count != b.ItemSpawnWeights.Count)
            {
                IssaPluginPlugin.Log.LogWarning(
                    $"SpawnWeightsMessages have unequal number of entries: {a.ItemSpawnWeights.Count} vs. {b.ItemSpawnWeights.Count}"
                );
                return true; // Probably shouldn't happen, but let's update to correct.
            }

            foreach (var (itemType, spawnWeight) in a.ItemSpawnWeights)
            {
                if (!b.ItemSpawnWeights.ContainsKey(itemType))
                {
                    // Doesn't have an entry... shouldn't happen, but if it does, they should be updated.
                    IssaPluginPlugin.Log.LogError(
                        $"SpawnWeightsMessage is missing entry for item type ${itemType}"
                    );
                    return true;
                }

                if (a.ItemSpawnWeights[itemType] != b.ItemSpawnWeights[itemType])
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
                CustomItemDefinition itemDef = ItemRegistry.CustomItemDefinitionMap[itemType];
                itemDef.SpawnWeight = spawnWeight;
            }

            IssaPluginPlugin.Log.LogDebug($"[SpawnWeights] Received from host: {msg.ToString()}");
        }
    }
}
