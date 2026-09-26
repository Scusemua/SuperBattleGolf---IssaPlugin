using System.Reflection;
using HarmonyLib;
using IssaPlugin.Items;

namespace IssaPlugin.Patches
{
    /// <summary>
    /// The minigun does not set ItemUseType.Regular, so the elephant-gun shoot clip
    /// stays off. While a round is still in the gun, this keeps it from being holstered.
    /// Once the last round has been spent, deselect has to succeed or the empty slot
    /// stays selected. RemoveItemAt retries that deselect in the same call.
    /// </summary>
    [HarmonyPatch(typeof(PlayerInventory), "CanDeselectItem")]
    static class MinigunDeselectPatch
    {
        static void Postfix(PlayerInventory __instance, ref bool __result)
        {
            if (!Firearm.IsHolding(__instance, ItemRegistry.MinigunItemType))
                return;

            int slot = __instance.EquippedItemIndex;
            if (slot < 0 || slot >= __instance.slots.Count)
                return;

            var held = __instance.slots[slot];
            if (held.itemType == ItemRegistry.MinigunItemType && held.remainingUses > 0)
                __result = false;
        }
    }

    /// <summary>
    /// Forces the base game's walk-speed clamp while the minigun is actually firing.
    /// The clamp is the same one ctrl/alt walk uses. It does not write IsHoldingWalk,
    /// so a real walk-key press is left alone when the burst ends.
    /// </summary>
    [HarmonyPatch]
    static class MinigunWalkPatch
    {
        // The |314 suffix is a compiler id and changes when the game method is recompiled.
        static MethodBase TargetMethod()
        {
            const string prefix = "<ProcessMovementInput>g__ShouldClampToWalkingSpeed";
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            foreach (var method in typeof(PlayerMovement).GetMethods(flags))
            {
                if (method.Name.StartsWith(prefix) && method.ReturnType == typeof(bool))
                    return method;
            }

            return null;
        }

        static void Postfix(PlayerMovement __instance, ref bool __result)
        {
            if (__result || __instance == null)
                return;

            if (
                Firearm.IsFiringBullets(
                    __instance.PlayerInfo?.Inventory,
                    ItemRegistry.MinigunItemType
                )
            )
                __result = true;
        }
    }
}
