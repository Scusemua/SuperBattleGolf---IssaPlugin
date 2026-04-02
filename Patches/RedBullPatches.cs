using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using IssaPlugin.Items;
using UnityEngine;

namespace IssaPlugin.Patches
{
    /// <summary>
    /// Multiplies movement speed while the Red Bull effect is active and
    /// the speed-boost status effect is running. Targets both the ground
    /// and air speed helper methods on PlayerMovement.
    ///
    /// The native coffee speed boost applies settings.SpeedBoostSpeedFactor.
    /// This postfix multiplies that result by RedBullExtraSpeedMultiplier on
    /// top, giving a total of: baseSpeed × SpeedBoostSpeedFactor × RedBullExtra.
    /// </summary>
    [HarmonyPatch]
    static class RedBullSpeedPatch
    {
        private static MethodBase _groundSpeedMb;
        private static MethodBase _airSpeedMb;

        static RedBullSpeedPatch()
        {
            var pmType = AccessTools.TypeByName("PlayerMovement");
            if (pmType == null)
            {
                IssaPluginPlugin.Log.LogWarning(
                    "[RedBull] PlayerMovement not found — speed patch skipped."
                );
                return;
            }
            _groundSpeedMb = AccessTools.Method(
                pmType,
                "<UpdateMovementSpeed>g__GetCurrentMoveSpeed|246_0"
            );
            _airSpeedMb = AccessTools.Method(
                pmType,
                "<UpdateMovementSpeed>g__GetTargetSpeedInAir|246_1"
            );
        }

        static IEnumerable<MethodBase> TargetMethods()
        {
            if (_groundSpeedMb != null)
                yield return _groundSpeedMb;
            if (_airSpeedMb != null)
                yield return _airSpeedMb;
        }

        static void Postfix(object __instance, ref float __result)
        {
            if (!RedBullBehaviour.IsActive)
                return;
            var local = GameManager.LocalPlayerMovement;
            if (local == null || !ReferenceEquals(__instance, local))
                return;
            // Only boost when the speed-boost status effect is actually running.
            if (local.SpeedBoostRemainingTime <= 0f)
                return;
            __result *= ModConfig.RedBull.ExtraSpeedMultiplier.Value;
        }
    }

    /// <summary>
    /// Adds a configurable bonus to upward jump velocity while Red Bull is active.
    /// Runs as a postfix on TriggerJumpInternal, after the game has already set
    /// this.Velocity from JumpUpwardsSpeed.
    /// </summary>
    [HarmonyPatch]
    static class RedBullJumpPatch
    {
        private static MethodBase _targetMb;
        private static PropertyInfo _velocityProp;

        static RedBullJumpPatch()
        {
            var pmType = AccessTools.TypeByName("PlayerMovement");
            if (pmType == null)
            {
                IssaPluginPlugin.Log.LogWarning(
                    "[RedBull] PlayerMovement not found — jump patch skipped."
                );
                return;
            }
            _targetMb = AccessTools.Method(pmType, "TriggerJumpInternal");
            _velocityProp = AccessTools.Property(pmType, "Velocity");
        }

        static MethodBase TargetMethod() => _targetMb;

        static void Postfix(object __instance)
        {
            if (!RedBullBehaviour.IsActive)
                return;
            var local = GameManager.LocalPlayerMovement;
            if (local == null || !ReferenceEquals(__instance, local))
                return;
            if (_velocityProp == null)
                return;
            var vel = (Vector3)_velocityProp.GetValue(local);
            vel.y += ModConfig.RedBull.JumpBonus.Value;
            _velocityProp.SetValue(local, vel);
        }
    }
}
