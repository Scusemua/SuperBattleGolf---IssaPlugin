using Mirror;
using UnityEngine;

namespace IssaPlugin.Items
{
    /// Attached to every player object via NetworkBridgePatches.
    ///
    /// Receives CubeBallRequestMessage from the owning client, validates that
    /// the item is equipped and the target is valid, consumes the item, then
    /// calls CubeBallHelper.ServerApplyCube to start the effect.
    public class CubeBallNetworkBridge : NetworkBridgeBase
    {
        private PlayerInventory _inventory;

        private void Awake() => _inventory = GetComponent<PlayerInventory>();

        // ================================================================
        //  Server-side request handler
        //  Called from NetworkManagerPatches when CubeBallRequestMessage arrives.
        // ================================================================

        public void ServerHandleRequest(uint targetNetId, int equippedSlotIndex)
        {
            if (!isServer)
                return;

            if (_inventory == null)
                return;

            if (
                ItemRegistry.GetItemTypeAtSlot(_inventory, equippedSlotIndex)
                != ItemRegistry.CubeBallItemType
            )
            {
                IssaPluginPlugin.Log.LogWarning(
                    "[CubeBall] ServerHandleRequest: item not at expected slot."
                );
                return;
            }

            if (targetNetId == netId)
            {
                IssaPluginPlugin.Log.LogWarning(
                    "[CubeBall] ServerHandleRequest: self-targeting rejected."
                );
                return;
            }

            if (!NetworkServer.spawned.ContainsKey(targetNetId))
            {
                IssaPluginPlugin.Log.LogWarning(
                    $"[CubeBall] ServerHandleRequest: target netId {targetNetId} not found."
                );
                return;
            }

            var targetInventory = NetworkServer
                .spawned[targetNetId]
                .GetComponent<PlayerInventory>();
            if (targetInventory == null)
            {
                IssaPluginPlugin.Log.LogWarning(
                    "[CubeBall] ServerHandleRequest: target has no PlayerInventory."
                );
                return;
            }

            // Validate the target has a ball before consuming the item.
            var ball = targetInventory.PlayerInfo?.AsGolfer?.OwnBall;
            if (ball == null)
            {
                IssaPluginPlugin.Log.LogWarning(
                    "[CubeBall] ServerHandleRequest: target has no GolfBall."
                );
                return;
            }

            ItemHelper.ConsumeItemAtSlot(_inventory, equippedSlotIndex);

            float duration = ModConfig.CubeBall.Duration.Value;
            CubeBallHelper.ServerApplyCube(targetNetId, duration);

            IssaPluginPlugin.Log.LogInfo(
                $"[CubeBall] Applied cube to target netId={targetNetId} for {duration:F1}s."
            );
        }

        // ================================================================
        //  NetworkBridgeBase overrides
        // ================================================================

        public override void ServerHoleCleanup()
        {
            // Cleanup is global; handled by CubeBallHelper.ServerCleanupAll()
            // called from the first bridge instance that runs this (harmless to
            // call multiple times since the dict will be empty after the first).
            CubeBallHelper.ServerCleanupAll();
        }

        public override void ClientHoleCleanup()
        {
            CubeBallOverlay.Instance?.ForceClose();
        }
    }
}
