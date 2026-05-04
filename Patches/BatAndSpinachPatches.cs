using System.Collections;
using System.Reflection;
using HarmonyLib;
using IssaPlugin.Items;
using UnityEngine;

namespace IssaPlugin.Patches
{
    [HarmonyPatch]
    static class HitWithGolfSwingInternalPatch
    {
        internal static bool BatActive;
        internal static bool RecentHit;
        internal static bool WasRocketDriver;

        // N: the total velocity multiplier applied this hit (1.0 = no change)
        internal static float TotalMultiplier = 1f;

        private static float _originalMaxPowerSwingHitSpeed;
        private static float _originalMinPowerRocketDriverSwingHitSpeed;
        private static float _originalMaxPowerRocketDriverSwingHitSpeed;
        private static float _originalMinPowerRocketDriverPuttHitSpeed;
        private static float _originalMaxPowerRocketDriverPuttHitSpeed;
        private static float _pendingMultiplier = 1f;

        private static readonly FieldInfo MaxPowerSwingHitSpeedField = AccessTools.Field(
            typeof(SwingHittableSettings),
            "<MaxPowerSwingHitSpeed>k__BackingField"
        );
        private static readonly FieldInfo MinPowerRocketDriverSwingHitSpeedField = AccessTools.Field(
            typeof(SwingHittableSettings),
            "<MinPowerRocketDriverSwingHitSpeed>k__BackingField"
        );
        private static readonly FieldInfo MaxPowerRocketDriverSwingHitSpeedField = AccessTools.Field(
            typeof(SwingHittableSettings),
            "<MaxPowerRocketDriverSwingHitSpeed>k__BackingField"
        );
        private static readonly FieldInfo MinPowerRocketDriverPuttHitSpeedField = AccessTools.Field(
            typeof(SwingHittableSettings),
            "<MinPowerRocketDriverPuttHitSpeed>k__BackingField"
        );
        private static readonly FieldInfo MaxPowerRocketDriverPuttHitSpeedField = AccessTools.Field(
            typeof(SwingHittableSettings),
            "<MaxPowerRocketDriverPuttHitSpeed>k__BackingField"
        );

        static MethodBase TargetMethod() =>
            AccessTools.Method(typeof(Hittable), "HitWithGolfSwingInternal");

        private static IEnumerator TrackVelocityAfterHit(Rigidbody rb)
        {
            float elapsed = 0f;
            // Track for 5 seconds (covers most golf ball flight time)
            while (elapsed < 5f)
            {
                yield return null;
                elapsed += Time.deltaTime;
                if (rb == null)
                    yield break;
            }
            RecentHit = false;
            TotalMultiplier = 1f;
        }

        static void Prefix(
            Hittable __instance,
            PlayerGolfer hitter,
            float power,
            Vector3 worldDirection,
            bool isPutt,
            bool isRocketDriver
        )
        {
            BatActive = false;
            WasRocketDriver = false;
            _pendingMultiplier = 1f;

            if (hitter == null)
                return;

            var inv = hitter.PlayerInfo.Inventory;
            if (
                inv.GetEffectivelyEquippedItem(true) != ItemRegistry.BaseballBatItemType
                && !SpinachBehaviour.IsActive
            )
                return;

            bool isGolfBall = __instance.AsEntity.IsGolfBall;

            float extraPower = 1.0f;
            if (inv.GetEffectivelyEquippedItem(true) == ItemRegistry.BaseballBatItemType)
            {
                BatActive = true;
                float batMultiplier = isGolfBall
                    ? ModConfig.BaseballBat.GolfBallPowerMultiplier.Value
                    : ModConfig.BaseballBat.PowerMultiplier.Value;
                extraPower = batMultiplier - 1f;
                if (extraPower <= 0f)
                    extraPower = 1.0f;
            }

            if (SpinachBehaviour.IsActive)
            {
                float spinachMultiplier = isGolfBall
                    ? ModConfig.Spinach.GolfBallPowerMultiplier.Value
                    : ModConfig.Spinach.PowerMultiplier.Value;
                float spinachExtra = spinachMultiplier - 1f;
                if (spinachExtra <= 0f)
                    spinachExtra = 1.0f;

                extraPower *= spinachExtra;
            }

            // N is the total velocity multiplier: FINAL = N * base_velocity
            float N = 1f + extraPower;
            _pendingMultiplier = N;
            WasRocketDriver = isRocketDriver;

            // Scale the speed fields the base game uses so the network snapshot captures the
            // correct multiplied velocity. RocketDriver uses Remap(base, full, min, max, power),
            // so we scale both min and max; normal swings only use MaxPowerSwingHitSpeed.
            if (__instance.SwingSettings != null)
            {
                if (isRocketDriver)
                {
                    _originalMinPowerRocketDriverSwingHitSpeed = __instance.SwingSettings.MinPowerRocketDriverSwingHitSpeed;
                    _originalMaxPowerRocketDriverSwingHitSpeed = __instance.SwingSettings.MaxPowerRocketDriverSwingHitSpeed;
                    _originalMinPowerRocketDriverPuttHitSpeed = __instance.SwingSettings.MinPowerRocketDriverPuttHitSpeed;
                    _originalMaxPowerRocketDriverPuttHitSpeed = __instance.SwingSettings.MaxPowerRocketDriverPuttHitSpeed;
                    MinPowerRocketDriverSwingHitSpeedField.SetValue(__instance.SwingSettings, _originalMinPowerRocketDriverSwingHitSpeed * N);
                    MaxPowerRocketDriverSwingHitSpeedField.SetValue(__instance.SwingSettings, _originalMaxPowerRocketDriverSwingHitSpeed * N);
                    MinPowerRocketDriverPuttHitSpeedField.SetValue(__instance.SwingSettings, _originalMinPowerRocketDriverPuttHitSpeed * N);
                    MaxPowerRocketDriverPuttHitSpeedField.SetValue(__instance.SwingSettings, _originalMaxPowerRocketDriverPuttHitSpeed * N);
                }
                else
                {
                    _originalMaxPowerSwingHitSpeed = __instance.SwingSettings.MaxPowerSwingHitSpeed;
                    MaxPowerSwingHitSpeedField.SetValue(__instance.SwingSettings, _originalMaxPowerSwingHitSpeed * N);
                }
            }

            IssaPluginPlugin.Log.LogInfo(
                $"[BatSpinach Prefix] bat={BatActive} spinach={SpinachBehaviour.IsActive} rocketDriver={isRocketDriver} N={N:F2} power={power} isPutt={isPutt}"
            );
        }

        static void Postfix(Hittable __instance, PlayerGolfer hitter)
        {
            // Always restore speed fields, even if we bail early
            if (_pendingMultiplier > 1f && __instance.SwingSettings != null)
            {
                if (WasRocketDriver)
                {
                    MinPowerRocketDriverSwingHitSpeedField.SetValue(__instance.SwingSettings, _originalMinPowerRocketDriverSwingHitSpeed);
                    MaxPowerRocketDriverSwingHitSpeedField.SetValue(__instance.SwingSettings, _originalMaxPowerRocketDriverSwingHitSpeed);
                    MinPowerRocketDriverPuttHitSpeedField.SetValue(__instance.SwingSettings, _originalMinPowerRocketDriverPuttHitSpeed);
                    MaxPowerRocketDriverPuttHitSpeedField.SetValue(__instance.SwingSettings, _originalMaxPowerRocketDriverPuttHitSpeed);
                }
                else
                {
                    MaxPowerSwingHitSpeedField.SetValue(__instance.SwingSettings, _originalMaxPowerSwingHitSpeed);
                }
            }

            if (_pendingMultiplier <= 1f)
                return;
            if (!__instance.AsEntity.HasRigidbody)
                return;

            var rb = __instance.AsEntity.Rigidbody;
            TotalMultiplier = _pendingMultiplier;
            RecentHit = true;

            IssaPluginPlugin.Log.LogInfo(
                $"[BatSpinach Postfix] N={TotalMultiplier:F2} vel={rb.linearVelocity} (mag={rb.linearVelocity.magnitude:F2})"
            );

            IssaPluginPlugin.Instance.StartCoroutine(TrackVelocityAfterHit(rb));

            // OnFinishedSwinging handles decrement for player hits; golf ball hits don't trigger it.
            if (
                BatActive
                && __instance.AsEntity.IsGolfBall
                && hitter != null
                && hitter.isLocalPlayer
            )
            {
                var inventory = hitter.PlayerInfo.Inventory;
                int slotIndex = inventory.EquippedItemIndex;
                if (slotIndex >= 0)
                {
                    IssaPluginPlugin.Log.LogInfo(
                        $"[HitWithGolfSwingInternalPatch PostFix] ConsumeItemAtSlot called!"
                    );
                    ItemHelper.ConsumeItemAtSlot(inventory, slotIndex);
                }
            }
        }
    }

    [HarmonyPatch]
    static class HitWithSwingProjectilePatch
    {
        static MethodBase TargetMethod() =>
            AccessTools.Method(typeof(Hittable), "HitWithSwingProjectile");

        static void Prefix(
            Hittable __instance,
            Vector3 localHitPosition,
            Vector3 worldHitDirection,
            float normalizedHitSpeed,
            Hittable hitter,
            bool wasHoming,
            bool wasSwungByRocketDriver,
            PlayerGolfer responsiblePlayer
        )
        {
            if (!HitWithGolfSwingInternalPatch.RecentHit)
                return;

            var rb = __instance.AsEntity.HasRigidbody ? __instance.AsEntity.Rigidbody : null;
            IssaPluginPlugin.Log.LogInfo(
                $"[SwingProjectileHit] worldHitDir={worldHitDirection} normalizedHitSpeed={normalizedHitSpeed:F3} currentVel={rb?.linearVelocity.ToString() ?? "no rb"}"
            );
        }
    }

    /// <summary>
    /// After a bat/spinach hit, replaces the explicit-Euler quadratic drag with a stable
    /// implicit scheme AND scales the drag coefficient by 1/N² (where N is our velocity
    /// multiplier). This ensures the ball decelerates at the same rate as a normal hit but
    /// from N× the starting speed, so it travels N× further.
    ///
    /// Math: with k_eff = k/N² and v_start = N·v₀, each tick produces v = N·v_normal(t),
    /// so the ball lands exactly N times further than without the item.
    /// </summary>
    [HarmonyPatch]
    static class ApplyAirDampingStablePatch
    {
        static MethodBase TargetMethod() => AccessTools.Method(typeof(Hittable), "ApplyAirDamping");

        static bool Prefix(
            Hittable __instance,
            float linearAirDragFactor,
            float rocketDriverSwingLinearAirDragFactor,
            bool shouldApplyWind
        )
        {
            if (!HitWithGolfSwingInternalPatch.RecentHit)
                return true;
            if (!__instance.AsEntity.IsGolfBall || !__instance.AsEntity.HasRigidbody)
                return true;

            float N = HitWithGolfSwingInternalPatch.TotalMultiplier;
            if (N <= 1f)
                return true;

            var rb = __instance.AsEntity.Rigidbody;
            Vector3 vel = rb.linearVelocity;
            float sqrMag = vel.sqrMagnitude;

            // Scale drag coefficient by 1/N² so the ball maintains N× the normal trajectory.
            // Mirror the base game's choice: rocket-driver hits use the rocket-driver drag factor.
            float kBase = HitWithGolfSwingInternalPatch.WasRocketDriver
                ? rocketDriverSwingLinearAirDragFactor
                : linearAirDragFactor;
            float kEff = kBase / (N * N);
            float dt = Time.fixedDeltaTime;

            // Implicit Euler: d = kEff·v²·dt / (1 + kEff·v²·dt), always in [0, 1)
            float num = kEff * sqrMag * dt;
            float d = num / (1f + num);

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

            IssaPluginPlugin.Log.LogInfo(
                $"[OnFinishedSwingingPatch PostFix] ConsumeItemAtSlot called!"
            );

            ItemHelper.ConsumeItemAtSlot(inventory, slotIndex);
        }
    }
}
