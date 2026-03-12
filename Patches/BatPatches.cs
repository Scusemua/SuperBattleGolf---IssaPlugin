using System.Reflection;
using HarmonyLib;
using IssaPlugin.Items;
using UnityEngine;

namespace IssaPlugin.Patches
{
    [HarmonyPatch]
    static class GetEffectivelyEquippedItemPatch
    {
        static MethodBase TargetMethod() =>
            AccessTools.Method(typeof(PlayerInventory), "GetEffectivelyEquippedItem");

        static void Postfix(
            PlayerInventory __instance,
            bool ignoreEquipmentHiding,
            ref ItemType __result
        )
        {
            if (!ignoreEquipmentHiding && ItemRegistry.IsCustomItem(__result))
                __result = ItemType.None;
        }
    }

    [HarmonyPatch]
    static class HitWithGolfSwingInternalPatch
    {
        internal static bool BatActive;
        private static Vector3 _velocityBefore;
        private static Vector3 _angularVelocityBefore;

        static MethodBase TargetMethod() =>
            AccessTools.Method(typeof(Hittable), "HitWithGolfSwingInternal");

        static void Prefix(Hittable __instance, PlayerGolfer hitter)
        {
            BatActive = false;
            if (hitter == null)
                return;

            var inv = hitter.PlayerInfo.Inventory;
            if (inv.GetEffectivelyEquippedItem(true) != BatItem.BatItemType)
                return;

            BatActive = true;
            if (__instance.AsEntity.HasRigidbody)
            {
                _velocityBefore = __instance.AsEntity.Rigidbody.linearVelocity;
                _angularVelocityBefore = __instance.AsEntity.Rigidbody.angularVelocity;
            }
        }

        static void Postfix(Hittable __instance)
        {
            if (!BatActive)
                return;

            if (!__instance.AsEntity.HasRigidbody)
                return;

            float extra = Configuration.BaseballBatPowerMultiplier.Value - 1f;
            if (extra <= 0f)
                return;

            var rb = __instance.AsEntity.Rigidbody;
            rb.linearVelocity += (rb.linearVelocity - _velocityBefore) * extra;
            rb.angularVelocity += (rb.angularVelocity - _angularVelocityBefore) * extra;
        }
    }

    [HarmonyPatch]
    static class BecomeSwingProjectilePatch
    {
        static MethodBase TargetMethod() =>
            AccessTools.Method(typeof(Hittable), "BecomeSwingProjectile");

        static bool Prefix()
        {
            return !HitWithGolfSwingInternalPatch.BatActive;
        }
    }

    [HarmonyPatch]
    static class OnFinishedSwingingPatch
    {
        static MethodBase TargetMethod() =>
            AccessTools.Method(typeof(PlayerGolfer), "OnFinishedSwinging");

        static void Postfix(PlayerGolfer __instance)
        {
            if (!__instance.isLocalPlayer)
                return;

            var inventory = __instance.PlayerInfo.Inventory;
            if (inventory.GetEffectivelyEquippedItem(true) != BatItem.BatItemType)
                return;

            int slotIndex = inventory.EquippedItemIndex;
            if (slotIndex < 0)
                return;

            ItemHelper.DecrementAndRemove(inventory, slotIndex);
        }
    }
}
