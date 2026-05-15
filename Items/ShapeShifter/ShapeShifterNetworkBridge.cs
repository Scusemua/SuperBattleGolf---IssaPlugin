using Mirror;
using UnityEngine;

namespace IssaPlugin.Items
{
    /// Attached to every player object via NetworkBridgePatches.
    ///
    /// Receives ShapeShifterRequestMessage from the owning client, validates that
    /// the item is equipped and the target is valid, consumes the item, then
    /// calls ShapeShifterHelper.ServerApplyCube to start the effect.
    public class ShapeShifterNetworkBridge : NetworkBridgeBase
    {
        private PlayerInventory _inventory;

        private void Awake() => _inventory = GetComponent<PlayerInventory>();

        // ================================================================
        //  Server-side request handler
        //  Called from NetworkManagerPatches when ShapeShifterRequestMessage arrives.
        // ================================================================

        public void ServerHandleRequest(uint targetNetId, int equippedSlotIndex)
        {
            if (!isServer)
                return;

            if (_inventory == null)
                return;

            if (
                ItemRegistry.GetItemTypeAtSlot(_inventory, equippedSlotIndex)
                != ItemRegistry.ShapeShifterItemType
            )
            {
                IssaPluginPlugin.Log.LogWarning(
                    "[ShapeShifter] ServerHandleRequest: item not at expected slot."
                );
                return;
            }

            if (targetNetId == netId)
            {
                IssaPluginPlugin.Log.LogWarning(
                    "[ShapeShifter] ServerHandleRequest: self-targeting rejected."
                );
                return;
            }

            if (!NetworkServer.spawned.ContainsKey(targetNetId))
            {
                IssaPluginPlugin.Log.LogWarning(
                    $"[ShapeShifter] ServerHandleRequest: target netId {targetNetId} not found."
                );
                return;
            }

            var targetInventory = NetworkServer
                .spawned[targetNetId]
                .GetComponent<PlayerInventory>();
            if (targetInventory == null)
            {
                IssaPluginPlugin.Log.LogWarning(
                    "[ShapeShifter] ServerHandleRequest: target has no PlayerInventory."
                );
                return;
            }

            // Validate the target has a ball before consuming the item.
            var ball = targetInventory.PlayerInfo?.AsGolfer?.OwnBall;
            if (ball == null)
            {
                IssaPluginPlugin.Log.LogWarning(
                    "[ShapeShifter] ServerHandleRequest: target has no GolfBall."
                );
                return;
            }

            ItemHelper.ConsumeItemAtSlot(_inventory, equippedSlotIndex);

            float duration = ModConfig.ShapeShifter.Duration.Value;
            ShapeShifterHelper.ServerApplyCube(targetNetId, duration);

            IssaPluginPlugin.Log.LogInfo(
                $"[ShapeShifter] Applied cube to target netId={targetNetId} for {duration:F1}s."
            );
        }

        // ================================================================
        //  NetworkBridgeBase overrides
        // ================================================================

        public override void ServerHoleCleanup()
        {
            // Cleanup is global; handled by ShapeShifterHelper.ServerCleanupAll()
            // called from the first bridge instance that runs this (harmless to
            // call multiple times since the dict will be empty after the first).
            ShapeShifterHelper.ServerCleanupAll();
        }

        public override void ClientHoleCleanup()
        {
            ShapeShifterOverlay.Instance?.ForceClose();
            ShapeShifterOverlay.Instance?.SetCubed(false);
        }
    }
}
