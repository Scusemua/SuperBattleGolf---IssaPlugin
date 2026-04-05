using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using System.Text;
using HarmonyLib;
using IssaPlugin.Items;
using UnityEngine;

namespace IssaPlugin.Patches
{
    [HarmonyPatch]
    static class HitWithGolfSwingInternalPatch
    {
        internal static bool BatActive;
        private static Vector3 _velocityBefore;
        private static Vector3 _angularVelocityBefore;

        // private static float _originalMaxPowerSwingHitSpeed;
        private static readonly FieldInfo MaxPowerSwingHitSpeedField = AccessTools.Field(
            typeof(SwingHittableSettings),
            "<MaxPowerSwingHitSpeed>k__BackingField"
        );

        static MethodBase TargetMethod() =>
            AccessTools.Method(typeof(Hittable), "HitWithGolfSwingInternal");

        public static string PropertyList(this object obj)
        {
            var props = obj.GetType().GetProperties();
            var sb = new StringBuilder();
            foreach (var p in props)
            {
                sb.AppendLine(p.Name + ": " + p.GetValue(obj, null));
            }
            return sb.ToString();
        }

        static void Prefix(Hittable __instance, PlayerGolfer hitter)
        {
            BatActive = false;
            if (hitter == null)
                return;

            var inv = hitter.PlayerInfo.Inventory;
            if (
                inv.GetEffectivelyEquippedItem(true) != ItemRegistry.BaseballBatItemType
                && !SpinachBehaviour.IsActive
            )
                return;

            float extraPower = 1.0f;
            if (inv.GetEffectivelyEquippedItem(true) == ItemRegistry.BaseballBatItemType)
            {
                BatActive = true;
                extraPower = ModConfig.BaseballBat.PowerMultiplier.Value - 1f;
                if (extraPower <= 0f)
                    extraPower = 1.0f;
            }

            if (SpinachBehaviour.IsActive)
            {
                float extraPowerFromSpinach = ModConfig.Spinach.PowerMultiplier.Value - 1f;
                if (extraPowerFromSpinach <= 0f)
                    extraPowerFromSpinach = 1.0f;

                IssaPluginPlugin.Log.LogInfo(
                    $"[BatSpinachPatch] Extra power from spinach: {extraPowerFromSpinach}"
                );

                extraPower *= extraPowerFromSpinach;
            }

            if (__instance.AsEntity.HasRigidbody)
            {
                _velocityBefore = __instance.AsEntity.Rigidbody.linearVelocity;
                _angularVelocityBefore = __instance.AsEntity.Rigidbody.angularVelocity;
            }
        }

        static void Postfix(Hittable __instance)
        {
            if (!BatActive && !SpinachBehaviour.IsActive)
                return;

            if (!__instance.AsEntity.HasRigidbody)
                return;

            float extraPower = 1.0f;
            if (BatActive)
            {
                extraPower = ModConfig.BaseballBat.PowerMultiplier.Value - 1f;
                if (extraPower <= 0f)
                    extraPower = 1.0f;

                IssaPluginPlugin.Log.LogInfo(
                    $"[BatSpinachPatch] Extra power from bat: ${extraPower}"
                );
            }

            if (SpinachBehaviour.IsActive)
            {
                float extraPowerFromSpinach = ModConfig.Spinach.PowerMultiplier.Value - 1f;
                if (extraPowerFromSpinach <= 0f)
                    extraPowerFromSpinach = 1.0f;

                IssaPluginPlugin.Log.LogInfo(
                    $"[BatSpinachPatch] Extra power from spinach: {extraPowerFromSpinach}"
                );

                extraPower *= extraPowerFromSpinach;
            }

            IssaPluginPlugin.Log.LogInfo(
                $"[BatSpinachPatch] Total extra power from bat+spinach: {extraPower}"
            );

            var rb = __instance.AsEntity.Rigidbody;

            IssaPluginPlugin.Log.LogInfo(
                $"[BatSpinachPatch] rb.linearVelocity={rb.linearVelocity} (magnitude={rb.linearVelocity.magnitude}), velocityBefore={_velocityBefore} (magnitude={_velocityBefore.magnitude})"
            );

            IssaPluginPlugin.Log.LogInfo(
                $"[BatSpinachPatch] rb.angularVelocity={rb.angularVelocity} (magnitude={rb.linearVelocity.magnitude}), velocityBefore={_angularVelocityBefore} (magnitude={_angularVelocityBefore.magnitude})"
            );

            var swingSettings = __instance.SwingSettings;
            IssaPluginPlugin.Log.LogInfo(
                $"[BatSpinachpatch] SwingSettings: {PropertyList(swingSettings)}"
            );

            var additionalLinearVelocity = (rb.linearVelocity - _velocityBefore) * extraPower;
            var additionalAngularVelocity =
                (rb.angularVelocity - _angularVelocityBefore) * extraPower;

            IssaPluginPlugin.Log.LogInfo(
                $"[BatSpinachPatch] Additional linear velocity: {additionalLinearVelocity} (magnitude={additionalLinearVelocity.magnitude}). Additional angular velocity: {additionalAngularVelocity} (magnitude={additionalAngularVelocity.magnitude})."
            );

            rb.linearVelocity += additionalLinearVelocity;
            rb.angularVelocity += additionalAngularVelocity;

            IssaPluginPlugin.Log.LogInfo(
                $"[BatSpinachPatch] Velocity set to: {rb.linearVelocity.magnitude}"
            );
        }
    }

    [HarmonyPatch]
    static class BecomeSwingProjectilePatch
    {
        static MethodBase TargetMethod() =>
            AccessTools.Method(typeof(Hittable), "BecomeSwingProjectile");

        static bool Prefix()
        {
            return !HitWithGolfSwingInternalPatch.BatActive || !SpinachBehaviour.IsActive;
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
            if (inventory.GetEffectivelyEquippedItem(true) != ItemRegistry.BaseballBatItemType)
                return;

            int slotIndex = inventory.EquippedItemIndex;
            if (slotIndex < 0)
                return;

            ItemHelper.DecrementAndRemove(inventory, slotIndex);
        }
    }
}
