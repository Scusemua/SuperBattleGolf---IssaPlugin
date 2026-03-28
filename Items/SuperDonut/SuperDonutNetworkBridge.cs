using System.Collections;
using System.Collections.Generic;
using Mirror;
using UnityEngine;

namespace IssaPlugin.Items
{
    /// <summary>
    /// Attached to every player object via NetworkBridgePatches.
    ///
    /// SERVER SIDE: On activation, fires one orbital laser at every other player
    /// simultaneously (staggered by a small delay), consuming the item.
    ///
    /// CLIENT SIDE: No interactive session — the item fires instantly.
    /// </summary>
    public class SuperDonutNetworkBridge : NetworkBridgeBase
    {
        // Stagger between successive laser activations (seconds).
        private const float LaserStaggerDelay = 0.4f;

        // ================================================================
        //  Client → Server
        // ================================================================

        public void ServerFireBarrage()
        {
            if (!isServer)
                return;

            var inventory = GetComponent<PlayerInventory>();
            if (inventory == null)
                return;

            var equipped = inventory.GetEffectivelyEquippedItem(true);
            if (equipped != ItemRegistry.SuperDonutItemType)
            {
                IssaPluginPlugin.Log.LogWarning(
                    "[SuperDonut] Player does not have Super Donut equipped."
                );
                return;
            }

            ItemHelper.ConsumeEquippedItem(inventory);

            StartCoroutine(ServerBarrageCoroutine(inventory));
        }

        // ================================================================
        //  Server internals
        // ================================================================

        private IEnumerator ServerBarrageCoroutine(PlayerInventory owner)
        {
            var targets = CollectTargets(owner);

            if (targets.Count == 0)
            {
                IssaPluginPlugin.Log.LogInfo("[SuperDonut] No valid targets found.");
                NetworkServer.SendToAll(new SuperDonutFiredMessage { TargetCount = 0 });
                yield break;
            }

            IssaPluginPlugin.Log.LogInfo(
                $"[SuperDonut] Firing barrage at {targets.Count} player(s)."
            );

            int useIndex = 0;
            foreach (var hittable in targets)
            {
                var itemUseId = new ItemUseId(
                    owner.PlayerInfo.PlayerId.Guid,
                    ++useIndex,
                    ItemType.OrbitalLaser
                );

                OrbitalLaserManager.ServerActivateLaser(hittable, Vector3.zero, owner, itemUseId);

                if (useIndex < targets.Count)
                    yield return new WaitForSeconds(LaserStaggerDelay);
            }

            NetworkServer.SendToAll(new SuperDonutFiredMessage { TargetCount = targets.Count });

            IssaPluginPlugin.Log.LogInfo("[SuperDonut] Barrage complete.");
        }

        /// Returns the <see cref="Hittable"/> for every connected player except <paramref name="owner"/>.
        private static List<Hittable> CollectTargets(PlayerInventory owner)
        {
            var results = new List<Hittable>();

            foreach (var conn in NetworkServer.connections.Values)
            {
                var identity = conn.identity;
                if (identity == null)
                    continue;

                var targetInventory = identity.GetComponent<PlayerInventory>();
                if (targetInventory == null || targetInventory == owner)
                    continue;

                var hittable = targetInventory.PlayerInfo?.AsHittable;
                if (hittable == null)
                    continue;

                results.Add(hittable);
            }

            return results;
        }

        // ================================================================
        //  NetworkBridgeBase overrides
        // ================================================================

        public override void ServerHoleCleanup() { }

        public override void ClientHoleCleanup() { }
    }
}
