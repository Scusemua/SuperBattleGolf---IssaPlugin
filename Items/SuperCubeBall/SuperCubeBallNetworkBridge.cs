using Mirror;
using UnityEngine;

namespace IssaPlugin.Items
{
    /// Attached to every player object via NetworkBridgePatches.
    ///
    /// On activation, cubes every other player's golf ball simultaneously by
    /// calling CubeBallHelper.ServerApplyCube for each non-self target.
    /// No interactive session — the effect fires instantly.
    public class SuperCubeBallNetworkBridge : NetworkBridgeBase
    {
        private PlayerInventory _inventory;

        private void Awake() => _inventory = GetComponent<PlayerInventory>();

        // ================================================================
        //  Server-side request handler
        //  Called from NetworkManagerPatches when SuperCubeBallRequestMessage arrives.
        // ================================================================

        public void ServerHandleRequest(int equippedSlotIndex)
        {
            if (!isServer)
                return;

            if (_inventory == null)
                return;

            if (
                ItemRegistry.GetItemTypeAtSlot(_inventory, equippedSlotIndex)
                != ItemRegistry.SuperCubeBallItemType
            )
            {
                IssaPluginPlugin.Log.LogWarning(
                    "[SuperCubeBall] ServerHandleRequest: item not at expected slot."
                );
                return;
            }

            ItemHelper.ConsumeItemAtSlot(_inventory, equippedSlotIndex);

            float duration = ModConfig.SuperCubeBall.Duration.Value;
            int targetCount = 0;

            foreach (var conn in NetworkServer.connections.Values)
            {
                var identity = conn?.identity;
                if (identity == null)
                    continue;

                if (identity.netId == netId && !ModConfig.SuperCubeBall.AffectSelf.Value)
                    continue;

                var targetInventory = identity.GetComponent<PlayerInventory>();
                if (targetInventory == null)
                    continue;

                // Only cube players who actually have a ball in play.
                if (targetInventory.PlayerInfo?.AsGolfer?.OwnBall == null)
                    continue;

                CubeBallHelper.ServerApplyCube(identity.netId, duration);
                targetCount++;
            }

            IssaPluginPlugin.Log.LogInfo(
                $"[SuperCubeBall] Cubed {targetCount} ball(s) for {duration:F1}s."
            );
        }

        // ================================================================
        //  NetworkBridgeBase overrides
        // ================================================================

        public override void ServerHoleCleanup() { }

        public override void ClientHoleCleanup() { }
    }
}
