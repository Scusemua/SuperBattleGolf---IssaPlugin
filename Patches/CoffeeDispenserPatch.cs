using System.Reflection;
using HarmonyLib;
using IssaPlugin.Items;
using Mirror;
using UnityEngine;

namespace IssaPlugin.Patches
{
    /// <summary>
    /// With a configurable probability, replaces the coffee in a PhysicalItem pickup
    /// with a Red Bull when the server resolves the player picking it up.
    ///
    /// Flow: ItemDispenser (async) → spawns PhysicalItem → player interacts →
    /// CmdGiveToPlayer (server) → UserCode_CmdGiveToPlayer → ServerTryAddItem.
    /// We intercept at UserCode_CmdGiveToPlayer to swap itemType before the vanilla
    /// ServerTryAddItem call runs. ServerTryAddItemPatch then routes the RedBull
    /// custom item type through DirectAddCustomItem automatically.
    /// </summary>
    [HarmonyPatch]
    static class CoffeeDispenserRedBullPatch
    {
        private static readonly FieldInfo ItemTypeField =
            AccessTools.Field(typeof(PhysicalItem), "itemType");

        static MethodBase TargetMethod() =>
            AccessTools.Method(
                typeof(PhysicalItem),
                "UserCode_CmdGiveToPlayer__PlayerInventory__NetworkConnectionToClient"
            );

        static void Prefix(PhysicalItem __instance)
        {
            if (!NetworkServer.active)
                return;
            if ((ItemType)ItemTypeField.GetValue(__instance) != ItemType.Coffee)
                return;
            if (!(Random.value < Configuration.RedBullDispenserChance.Value))
                return;

            ItemTypeField.SetValue(__instance, ItemRegistry.RedBullItemType);
            IssaPluginPlugin.Log.LogInfo(
                "[CoffeeDispenser] Red Bull substituted for coffee pickup."
            );
        }
    }
}
