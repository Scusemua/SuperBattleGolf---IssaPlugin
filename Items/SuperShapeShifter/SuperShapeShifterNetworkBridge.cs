using Mirror;
using UnityEngine;

namespace IssaPlugin.Items
{
    /// Attached to every player object via NetworkBridgePatches.
    ///
    /// On activation, cubes every other player's golf ball simultaneously by
    /// calling ShapeShifterHelper.ServerApplyCube for each non-self target.
    /// No interactive session — the effect fires instantly.
    public class SuperShapeShifterNetworkBridge : NetworkBridgeBase
    {
        private PlayerInventory _inventory;

        private void Awake() => _inventory = GetComponent<PlayerInventory>();

        // ================================================================
        //  Server-side request handler
        //  Called from NetworkManagerPatches when SuperShapeShifterRequestMessage arrives.
        // ================================================================

        public void ServerHandleRequest(int equippedSlotIndex)
        {
            if (!isServer)
                return;

            if (_inventory == null)
                return;

            if (
                ItemRegistry.GetItemTypeAtSlot(_inventory, equippedSlotIndex)
                != ItemRegistry.SuperShapeShifterItemType
            )
            {
                IssaPluginPlugin.Log.LogWarning(
                    "[SuperShapeShifter] ServerHandleRequest: item not at expected slot."
                );
                return;
            }

            ItemHelper.ConsumeItemAtSlot(_inventory, equippedSlotIndex);

            float duration = ModConfig.SuperShapeShifter.Duration.Value;
            int targetCount = 0;

            foreach (var conn in NetworkServer.connections.Values)
            {
                var identity = conn?.identity;
                if (identity == null)
                    continue;

                if (identity.netId == netId && !ModConfig.SuperShapeShifter.AffectSelf.Value)
                    continue;

                var targetInventory = identity.GetComponent<PlayerInventory>();
                if (targetInventory == null)
                    continue;

                // Only cube players who actually have a ball in play.
                if (targetInventory.PlayerInfo?.AsGolfer?.OwnBall == null)
                    continue;

                ShapeShifterHelper.ServerApplyCube(identity.netId, duration, reroll: true);
                targetCount++;
            }

            IssaPluginPlugin.Log.LogInfo(
                $"[SuperShapeShifter] Cubed {targetCount} ball(s) for {duration:F1}s."
            );
        }

        // ================================================================
        //  NetworkBridgeBase overrides
        // ================================================================

        public override void ServerHoleCleanup() { }

        public override void ClientHoleCleanup() { }
    }
}
