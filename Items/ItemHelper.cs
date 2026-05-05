using System.Reflection;
using Mirror;
using UnityEngine;

namespace IssaPlugin.Items
{
    public static class ItemHelper
    {
        /// Layer mask used for ground raycasts. Public so AC130NetworkBridge
        /// can use it without duplicating the GetMask call.
        public static readonly int GroundLayerMask = LayerMask.GetMask("Default", "Terrain");
        private static readonly RaycastHit[] raycastHitBuffer = new RaycastHit[30];

        private static readonly MethodInfo DecrementMethod = typeof(PlayerInventory).GetMethod(
            "DecrementUseFromSlotAt",
            BindingFlags.NonPublic | BindingFlags.Instance
        );

        private static readonly MethodInfo RemoveMethod = typeof(PlayerInventory).GetMethod(
            "RemoveIfOutOfUses",
            BindingFlags.NonPublic | BindingFlags.Instance
        );

        public static void GiveItemToLocalPlayer(ItemType itemType, int uses, string logTag)
        {
            var inventory = GameManager.LocalPlayerInventory;
            if (inventory == null)
            {
                IssaPluginPlugin.Log.LogWarning($"[{logTag}] No local player inventory.");
                return;
            }

            if (NetworkServer.active)
            {
                // Host is always allowed to give themselves items.
                bool added = ItemRegistry.DirectAddCustomItem(inventory, itemType, uses);
                if (!added)
                    IssaPluginPlugin.Log.LogWarning(
                        $"[{logTag}] Failed to add item (inventory full?)."
                    );
            }
            else
            {
                // Non-host clients send a request to the server, which checks
                // AllowHotkeyItemGiving before granting.
                NetworkClient.Send(new GiveItemRequestMessage { ItemType = itemType, Uses = uses });
                IssaPluginPlugin.Log.LogInfo($"[{logTag}] Sent item request to server.");
            }
        }

        public static void DecrementAndRemove(PlayerInventory inventory, int slotIndex)
        {
            DecrementMethod?.Invoke(inventory, new object[] { slotIndex });
            RemoveMethod?.Invoke(inventory, new object[] { slotIndex });
        }


        private static readonly MethodInfo SetItemUseMethod = typeof(PlayerInventory).GetMethod(
            "SetCurrentItemUse",
            BindingFlags.NonPublic | BindingFlags.Instance
        );

        public static void SetCurrentItemUse(PlayerInventory inventory, ItemUseType type)
        {
            SetItemUseMethod?.Invoke(inventory, new object[] { type });
        }

        // public static void ApplyRecoil(
        //     PlayerInventory inventory,
        //     Vector3 shotDirection,
        //     float recoil
        // )
        // {
        //     if (recoil == 0f)
        //         return;
        //     inventory.PlayerInfo.Rigidbody.linearVelocity -= shotDirection.normalized * recoil;
        // }

        /// <summary>
        /// Server-side: consumes the item at an explicit <paramref name="slotIndex"/>.
        /// Use this overload when the slot index was transmitted in a network message,
        /// because <c>NetworkedEquippedItemIndex</c> is never synced client→server and
        /// <c>EquippedItemIndex</c> is not set on the server for remote-client objects.
        /// </summary>
        public static void ConsumeItemAtSlot(PlayerInventory inventory, int slotIndex)
        {
            if (slotIndex < 0)
                return;

            SetCurrentItemUse(inventory, ItemUseType.Regular);
            DecrementAndRemove(inventory, slotIndex);
            SetCurrentItemUse(inventory, ItemUseType.None);
        }

        /// Server-side convenience: wraps SetCurrentItemUse + DecrementAndRemove + SetCurrentItemUse.
        ///
        /// Only reliable for the host's own inventory. For remote-client inventories use
        /// <see cref="ConsumeItemAtSlot"/> with the slot index passed in the network message.
        public static void ConsumeEquippedItem(PlayerInventory inventory)
        {
            int slot =
                (!inventory.isLocalPlayer && NetworkServer.active)
                    ? inventory.PlayerInfo.NetworkedEquippedItemIndex
                    : inventory.EquippedItemIndex;
            if (slot < 0)
                return;

            SetCurrentItemUse(inventory, ItemUseType.Regular);
            DecrementAndRemove(inventory, slot);
            SetCurrentItemUse(inventory, ItemUseType.None);
        }

        /// <summary>
        /// Casts a ray straight down from well above the given XZ coordinate and
        /// returns the Y of the first ground hit, offset slightly upward so the
        /// marker disc sits on top of the surface rather than clipping into it.
        ///
        /// Falls back to <paramref name="fallbackY"/> when nothing is hit (e.g. the
        /// marker is dragged over a void or out-of-bounds area).
        ///
        /// Performance note: a single Physics.Raycast per frame is negligible.
        /// The layer mask excludes players and triggers so we only hit solid world
        /// geometry. Tighten the mask further if your project has a dedicated
        /// "Terrain" or "Ground" layer.
        /// </summary>
        public static float SampleTerrainY(float x, float z, float fallbackY)
        {
            // Cast from 2000 units above so even tall geometry is captured.
            const float castOriginHeight = 2000f;
            const float markerSurfaceOffset = 0.5f; // sits this far above the hit point

            var origin = new Vector3(x, castOriginHeight, z);
            int numHits = Physics.RaycastNonAlloc(
                origin,
                Vector3.down,
                raycastHitBuffer,
                castOriginHeight * 2f,
                GameManager.LayerSettings.PlayerGroundableMask,
                QueryTriggerInteraction.Ignore
            );
            if (numHits > 0)
            {
                // RaycastNonAlloc doesn't guarantee distance ordering. For a downward ray,
                // "closest to origin" means highest Y — the topmost walkable surface, which
                // is where a player falling from above would land.
                int closest = 0;
                for (int i = 1; i < numHits; i++)
                    if (raycastHitBuffer[i].distance < raycastHitBuffer[closest].distance)
                        closest = i;
                return raycastHitBuffer[closest].point.y + markerSurfaceOffset;
            }

            // Nothing hit — keep the marker at its current height so it doesn't
            // snap to zero over a void.
            return fallbackY;
        }
    }
}
