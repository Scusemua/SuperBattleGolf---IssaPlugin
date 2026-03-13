using System.Reflection;
using Mirror;
using UnityEngine;
using UnityEngine.InputSystem;

namespace IssaPlugin.Items
{
    public static class ItemHelper
    {
        /// Layer mask used for ground raycasts. Public so AC130NetworkBridge
        /// can use it without duplicating the GetMask call.
        public static readonly int GroundLayerMask = LayerMask.GetMask("Default", "Terrain");

        private static MethodInfo _cmdAddItemMethod;

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
                bool added = ItemRegistry.DirectAddCustomItem(inventory, itemType, uses);
                if (!added)
                    IssaPluginPlugin.Log.LogWarning(
                        $"[{logTag}] Failed to add item (inventory full?)."
                    );
            }
            else
            {
                if (_cmdAddItemMethod == null)
                {
                    _cmdAddItemMethod = typeof(PlayerInventory).GetMethod(
                        "CmdAddItem",
                        BindingFlags.NonPublic | BindingFlags.Instance
                    );
                }

                if (_cmdAddItemMethod != null)
                {
                    _cmdAddItemMethod.Invoke(inventory, new object[] { itemType });
                    IssaPluginPlugin.Log.LogInfo($"[{logTag}] Requested item via server command.");
                }
                else
                {
                    IssaPluginPlugin.Log.LogError($"[{logTag}] Could not find CmdAddItem method.");
                }
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

        /// Server-side convenience: wraps SetCurrentItemUse + DecrementAndRemove + SetCurrentItemUse.
        public static void ConsumeEquippedItem(PlayerInventory inventory)
        {
            int slot = inventory.EquippedItemIndex;
            if (slot < 0)
                return;

            SetCurrentItemUse(inventory, ItemUseType.Regular);
            DecrementAndRemove(inventory, slot);
            SetCurrentItemUse(inventory, ItemUseType.None);
        }
    }
}
