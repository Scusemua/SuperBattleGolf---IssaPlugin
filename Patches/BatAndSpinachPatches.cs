using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using System.Text;
using HarmonyLib;
using IssaPlugin.Items;
using UnityEngine;
using UnityEngine.Scripting;

namespace IssaPlugin.Patches
{
    [HarmonyPatch]
    static class HitWithGolfSwingInternalPatch
    {
        internal static bool BatActive;
        internal static bool RecentHit;
        private static Vector3 _velocityBefore;
        private static Vector3 _angularVelocityBefore;

        // private static float _originalMaxPowerSwingHitSpeed;
        private static readonly FieldInfo MaxPowerSwingHitSpeedField = AccessTools.Field(
            typeof(SwingHittableSettings),
            "<MaxPowerSwingHitSpeed>k__BackingField"
        );

        static MethodBase TargetMethod() =>
            AccessTools.Method(typeof(Hittable), "HitWithGolfSwingInternal");

        private static IEnumerator TemporarilyDisableCollisions(Rigidbody rb)
        {
            if (rb == null) yield break;
            rb.detectCollisions = false;
            yield return new WaitForSeconds(0.1f);
            if (rb != null) rb.detectCollisions = true;
        }

        private static IEnumerator TrackVelocityAfterHit(Rigidbody rb)
        {
            float elapsed = 0f;
            const float duration = 0.5f;
            int frame = 0;
            while (elapsed < duration)
            {
                yield return null;
                elapsed += Time.deltaTime;
                frame++;
                if (rb == null) yield break;
                IssaPluginPlugin.Log.LogInfo(
                    $"[BatSpinachPatch Track] frame={frame} t={elapsed:F3}s vel={rb.linearVelocity} (mag={rb.linearVelocity.magnitude:F2})"
                );
            }
            RecentHit = false;
        }

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

        static void Prefix(Hittable __instance, PlayerGolfer hitter, float power, Vector3 worldDirection, bool isPutt)
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

                extraPower *= extraPowerFromSpinach;
            }

            float gameSwingPowerMultiplier = MatchSetupRules.GetValue(MatchSetupRules.Rule.SwingPower);

            IssaPluginPlugin.Log.LogInfo(
                $"[BatSpinachPatch Prefix] batActive={BatActive}, spinachActive={SpinachBehaviour.IsActive}, power={power}, isPutt={isPutt}, worldDir={worldDirection}, gameSwingPower={gameSwingPowerMultiplier}, extraPower={extraPower}"
            );

            if (__instance.AsEntity.HasRigidbody)
            {
                _velocityBefore = __instance.AsEntity.Rigidbody.linearVelocity;
                _angularVelocityBefore = __instance.AsEntity.Rigidbody.angularVelocity;
                IssaPluginPlugin.Log.LogInfo(
                    $"[BatSpinachPatch Prefix] velocityBefore={_velocityBefore} (magnitude={_velocityBefore.magnitude})"
                );
            }
            else
            {
                IssaPluginPlugin.Log.LogWarning("[BatSpinachPatch Prefix] No rigidbody on hittable — _velocityBefore NOT updated");
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

            var swingDelta = rb.linearVelocity - _velocityBefore;
            var additionalLinearVelocity = swingDelta * extraPower;
            var additionalAngularVelocity = (rb.angularVelocity - _angularVelocityBefore) * extraPower;

            IssaPluginPlugin.Log.LogInfo(
                $"[BatSpinachPatch Postfix] extraPower={extraPower}"
            );
            IssaPluginPlugin.Log.LogInfo(
                $"[BatSpinachPatch Postfix] velocityBefore={_velocityBefore} (mag={_velocityBefore.magnitude:F2})"
            );
            IssaPluginPlugin.Log.LogInfo(
                $"[BatSpinachPatch Postfix] velocityAfterBaseGame={rb.linearVelocity} (mag={rb.linearVelocity.magnitude:F2})"
            );
            IssaPluginPlugin.Log.LogInfo(
                $"[BatSpinachPatch Postfix] swingDelta={swingDelta} (mag={swingDelta.magnitude:F2})"
            );
            IssaPluginPlugin.Log.LogInfo(
                $"[BatSpinachPatch Postfix] additionalLinearVelocity={additionalLinearVelocity} (mag={additionalLinearVelocity.magnitude:F2})"
            );

            rb.linearVelocity += additionalLinearVelocity;
            rb.angularVelocity += additionalAngularVelocity;

            IssaPluginPlugin.Log.LogInfo(
                $"[BatSpinachPatch Postfix] FINAL linearVelocity={rb.linearVelocity} (mag={rb.linearVelocity.magnitude:F2})"
            );

            RecentHit = true;
            IssaPluginPlugin.Instance.StartCoroutine(TrackVelocityAfterHit(rb));
            IssaPluginPlugin.Instance.StartCoroutine(TemporarilyDisableCollisions(rb));
        }
    }

    [HarmonyPatch]
    static class HitWithSwingProjectilePatch
    {
        static MethodBase TargetMethod() =>
            AccessTools.Method(typeof(Hittable), "HitWithSwingProjectile");

        static void Prefix(Hittable __instance, Vector3 localHitPosition, Vector3 worldHitDirection, float normalizedHitSpeed, Hittable hitter, bool wasHoming, bool wasSwungByRocketDriver, PlayerGolfer responsiblePlayer)
        {
            if (!HitWithGolfSwingInternalPatch.RecentHit)
                return;

            var rb = __instance.AsEntity.HasRigidbody ? __instance.AsEntity.Rigidbody : null;
            IssaPluginPlugin.Log.LogInfo(
                $"[SwingProjectileHit] worldHitDir={worldHitDirection} normalizedHitSpeed={normalizedHitSpeed:F3} currentVel={rb?.linearVelocity.ToString() ?? "no rb"} hitter={hitter?.name ?? "null"} responsible={responsiblePlayer?.name ?? "null"}"
            );
        }
    }

    /// <summary>
    /// Replaces the explicit-Euler quadratic air drag with implicit Euler immediately after a
    /// bat/spinach hit. The stock formula  v *= (1 - k·v²·dt)  goes unstable when k·v²·dt > 1,
    /// reversing the ball's direction. The implicit form  v /= (1 + k·v²·dt)  is unconditionally
    /// stable: the damping coefficient stays in [0, 1) for any speed.
    /// </summary>
    [HarmonyPatch]
    static class ApplyAirDampingStablePatch
    {
        static MethodBase TargetMethod() =>
            AccessTools.Method(typeof(Hittable), "ApplyAirDamping");

        static bool Prefix(Hittable __instance, float linearAirDragFactor, float rocketDriverSwingLinearAirDragFactor, bool shouldApplyWind)
        {
            if (!HitWithGolfSwingInternalPatch.RecentHit)
                return true;
            if (!__instance.AsEntity.IsGolfBall || !__instance.AsEntity.HasRigidbody)
                return true;

            var rb = __instance.AsEntity.Rigidbody;
            Vector3 vel = rb.linearVelocity;
            float sqrMag = vel.sqrMagnitude;
            float k = linearAirDragFactor;
            float dt = Time.fixedDeltaTime;

            // Implicit Euler: coefficient = k·v²·dt / (1 + k·v²·dt), always in [0, 1)
            float num = k * sqrMag * dt;
            float d = num / (1f + num);

            IssaPluginPlugin.Log.LogInfo(
                $"[AirDampStable] vel={vel.magnitude:F1} k={k:F6} d_explicit={k * sqrMag * dt:F3} d_stable={d:F3}"
            );

            rb.linearVelocity -= vel * d;
            return false;
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
