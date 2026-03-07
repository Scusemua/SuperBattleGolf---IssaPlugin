using System.Reflection;
using Mirror;

namespace IssaPlugin.Items
{
    public static class ItemHelper
    {
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
    }
}
