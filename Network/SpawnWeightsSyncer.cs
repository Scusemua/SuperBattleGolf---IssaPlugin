using System.Collections;
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

        private IEnumerator Start()
        {
            while (true)
            {
                yield return new WaitForSeconds(SyncInterval);
                if (NetworkServer.active)
                    BroadcastWeights();
            }
        }

        private static void BroadcastWeights()
        {
            // Re-inject custom weights into the live server pools.
            foreach (var settings in Resources.FindObjectsOfTypeAll<ItemSpawnerSettings>())
                settings.ResetRuntimeData();

            var msg = new SpawnWeightsMessage
            {
                Bat = Configuration.BaseballBatSpawnWeight.Value,
                Bomber = Configuration.BomberSpawnWeight.Value,
                Missile = Configuration.MissileSpawnWeight.Value,
                AC130 = Configuration.AC130SpawnWeight.Value,
                Freeze = Configuration.FreezeSpawnWeight.Value,
                LowGravity = Configuration.LowGravitySpawnWeight.Value,
                Sniper = Configuration.SniperRifleSpawnWeight.Value,
                Donut = Configuration.DonutSpawnWeight.Value,
            };

            NetworkServer.SendToAll(msg);

            IssaPluginPlugin.Log.LogDebug(
                $"[SpawnWeights] Synced — Bat={msg.Bat} Bomber={msg.Bomber} Missile={msg.Missile} "
                    + $"AC130={msg.AC130} Freeze={msg.Freeze} LowGravity={msg.LowGravity} "
                    + $"Sniper={msg.Sniper} Donut={msg.Donut}"
            );
        }

        /// Called on each client when a SpawnWeightsMessage arrives from the host.
        internal static void HandleSpawnWeights(SpawnWeightsMessage msg)
        {
            // Skip on the listen-server host; it already has the correct values.
            if (NetworkServer.active)
                return;

            Configuration.BaseballBatSpawnWeight.Value = msg.Bat;
            Configuration.BomberSpawnWeight.Value = msg.Bomber;
            Configuration.MissileSpawnWeight.Value = msg.Missile;
            Configuration.AC130SpawnWeight.Value = msg.AC130;
            Configuration.FreezeSpawnWeight.Value = msg.Freeze;
            Configuration.LowGravitySpawnWeight.Value = msg.LowGravity;
            Configuration.SniperRifleSpawnWeight.Value = msg.Sniper;
            Configuration.DonutSpawnWeight.Value = msg.Donut;

            IssaPluginPlugin.Log.LogDebug(
                $"[SpawnWeights] Received from host — Bat={msg.Bat} Bomber={msg.Bomber} "
                    + $"Missile={msg.Missile} AC130={msg.AC130} Freeze={msg.Freeze} "
                    + $"LowGravity={msg.LowGravity} Sniper={msg.Sniper} Donut={msg.Donut}"
            );
        }
    }
}
