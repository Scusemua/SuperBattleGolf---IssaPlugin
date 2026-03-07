using System.Reflection;
using IssaPlugin.Patches;
using Mirror;

namespace IssaPlugin.Items
{
    public static class BatItem
    {
        public static readonly ItemType BatItemType = (ItemType)100;

        private static MethodInfo _cmdAddItemMethod;

        public static void GiveBatToLocalPlayer()
        {
            var inventory = GameManager.LocalPlayerInventory;
            if (inventory == null)
            {
                IssaPluginPlugin.Log.LogWarning("[Bat] No local player inventory.");
                return;
            }

            if (NetworkServer.active)
            {
                bool added = InventoryPatches.DirectAddCustomItem(
                    inventory,
                    BatItemType,
                    Configuration.BaseballBatUses.Value
                );
                if (!added)
                    IssaPluginPlugin.Log.LogWarning("[Bat] Failed to add bat (inventory full?).");
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
                    _cmdAddItemMethod.Invoke(inventory, new object[] { BatItemType });
                    IssaPluginPlugin.Log.LogInfo("[Bat] Requested bat via server command.");
                }
                else
                {
                    IssaPluginPlugin.Log.LogError("[Bat] Could not find CmdAddItem method.");
                }
            }
        }
    }
}
