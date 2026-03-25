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

        /// Server-side convenience: wraps SetCurrentItemUse + DecrementAndRemove + SetCurrentItemUse.
        ///
        /// EquippedItemIndex is a client-local field and is not reliably set on the server
        /// for remote-client player objects. When running server-side for a non-local-player
        /// inventory, use NetworkedEquippedItemIndex instead (the synced value).
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
    }
}
