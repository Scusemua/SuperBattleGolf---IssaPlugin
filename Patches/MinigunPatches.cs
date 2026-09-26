using System.Reflection;
using HarmonyLib;
using IssaPlugin.Items;
using UnityEngine;

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
    /// Forces a fraction of normal move speed while the minigun is firing bullets.
    /// The fraction is MoveSpeedScale. The spin-up does not apply it.
    /// </summary>
    [HarmonyPatch]
    static class MinigunGroundSpeedPatch
    {
        static MethodBase TargetMethod() =>
            MinigunSpeed.FindSpeedMethod("<UpdateMovementSpeed>g__GetTargetSpeedOnGround");

        static void Postfix(PlayerMovement __instance, ref float __result) =>
            MinigunSpeed.Scale(__instance, ref __result);
    }

    [HarmonyPatch]
    static class MinigunAirSpeedPatch
    {
        static MethodBase TargetMethod() =>
            MinigunSpeed.FindSpeedMethod("<UpdateMovementSpeed>g__GetTargetSpeedInAir");

        static void Postfix(PlayerMovement __instance, ref float __result) =>
            MinigunSpeed.Scale(__instance, ref __result);
    }

    static class MinigunSpeed
    {
        internal static MethodBase FindSpeedMethod(string prefix)
        {
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            foreach (var method in typeof(PlayerMovement).GetMethods(flags))
            {
                if (method.Name.StartsWith(prefix) && method.ReturnType == typeof(float))
                    return method;
            }

            return null;
        }

        internal static void Scale(PlayerMovement movement, ref float speed)
        {
            if (movement == null)
                return;
            if (
                !Firearm.IsFiringBullets(
                    movement.PlayerInfo?.Inventory,
                    ItemRegistry.MinigunItemType
                )
            )
                return;

            speed *= Mathf.Clamp(ModConfig.Minigun.MoveSpeedScale.Value, 0f, 1f);
        }
    }
}
