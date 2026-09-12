using System.Reflection;
using HarmonyLib;
using IssaPlugin.Items;
using UnityEngine;

namespace IssaPlugin.Patches
{
    /// <summary>
    /// Harmony prefix on PlayerAnimatorIo.UpdateAimingAngle.
    ///
    /// UpdateAimingAngle drives the "Item aim pitch" animator parameter (how far up
    /// or down the player model tilts the weapon) and the networked aiming yaw
    /// offset. It selects the barrel origin/forward with a switch over
    /// <c>PlayerInfo.NetworkedEquippedItem</c> and returns early via <c>default:</c>
    /// for anything it does not recognise.
    ///
    /// For custom items that SyncVar stays <see cref="ItemType.None"/> — the same
    /// root cause documented in LocalPlayerUpdateIsEquipmentForceHiddenPatch — so
    /// the switch never matches and the aim pitch is never written. The result is a
    /// player model that holds the launcher but does not aim along the camera:
    /// the aim-in pose looks wrong compared to the base rocket launcher.
    ///
    /// Unlike the animator-controller and equipped-item patches, the value read here
    /// is a get-only property rather than a method argument, so it cannot be
    /// substituted with a <c>ref</c> parameter. This prefix instead reproduces the
    /// base game's RocketLauncher branch for the Golf Cart Launcher and skips the
    /// original, which would otherwise fall straight through to <c>default:</c>.
    ///
    /// The launcher is aimed exactly like the base rocket launcher — the fire path
    /// uses the same barrel helpers and layer mask — so the pitch it produces is
    /// identical to the weapon whose pose the animator is already playing.
    /// </summary>
    [HarmonyPatch]
    static class GolfCartLauncherAimAnimationPatch
    {
        // "Item aim pitch" is the animator float UpdateAimingAngle writes via its
        // private SetItemAimPitch. Hashed once here rather than reflecting into the
        // game's private static hash field.
        private static readonly int ItemAimPitchHash = Animator.StringToHash("Item aim pitch");

        private static readonly FieldInfo AnimatorField = AccessTools.Field(
            typeof(PlayerAnimatorIo),
            "animator"
        );

        private static readonly FieldInfo PlayerInfoField = AccessTools.Field(
            typeof(PlayerAnimatorIo),
            "playerInfo"
        );

        static MethodBase TargetMethod() =>
            AccessTools.Method(typeof(PlayerAnimatorIo), "UpdateAimingAngle");

        /// <returns>false to skip the original when this patch handled the item.</returns>
        static bool Prefix(PlayerAnimatorIo __instance, bool instant)
        {
            // Mirrors the original's own guards.
            if (!__instance.isLocalPlayer)
                return true;

            if (PlayerInfoField?.GetValue(__instance) is not PlayerInfo playerInfo)
                return true;

            var inventory = playerInfo.Inventory;
            if (inventory == null)
                return true;

            if (!inventory.IsAimingItem && !inventory.IsUsingItemAtAll)
                return true;

            // Only take over for our item; every other item keeps the base behaviour.
            if (
                inventory.GetEffectivelyEquippedItem(true)
                != ItemRegistry.GolfCartLauncherItemType
            )
                return true;

            if (AnimatorField?.GetValue(__instance) is not Animator animator)
                return true;

            var neck = playerInfo.NeckBone;
            if (neck == null)
                return true;

            // The base game's RocketLauncher branch, verbatim.
            Vector3 aimPoint = inventory.GetFirearmAimPoint(
                inventory.GetRocketLauncherBarrelFrontEndPosition(),
                inventory.GetRocketLauncherBarrelForward(),
                GameManager.ItemSettings.RocketLauncherMaxAimingDistance,
                GameManager.LayerSettings.RocketHittablesMask,
                out _
            );

            Vector3 toAim = aimPoint - neck.position;
            if (toAim.sqrMagnitude < 0.0001f)
                return false;

            float targetPitch = GolfCartLauncherAimMath.PitchDeg(toAim);

            // instant matches the original: snap on equip, smooth while tracking.
            // The smoothing rate (16) is the base game's own value.
            animator.SetFloat(
                ItemAimPitchHash,
                instant
                    ? targetPitch
                    : Mathf.Lerp(
                        animator.GetFloat(ItemAimPitchHash),
                        targetPitch,
                        16f * Time.deltaTime
                    )
            );

            // Keeps the upper body twisted toward the aim point for remote viewers.
            __instance.NetworkaimingYawOffset = GolfCartLauncherAimMath.WrapAngleDeg(
                GolfCartLauncherAimMath.YawDeg(toAim)
                    - GolfCartLauncherAimMath.YawDeg(__instance.transform.forward)
            );

            return false;
        }
    }

    /// <summary>
    /// Angle helpers for <see cref="GolfCartLauncherAimAnimationPatch"/>.
    ///
    /// These live outside the [HarmonyPatch] class deliberately. Harmony's analyzer
    /// treats every method in a patch class as a patch method and reports its
    /// parameters as non-ref patch parameters (Harmony003) when they are reassigned,
    /// even though these are ordinary private helpers Harmony never calls.
    /// </summary>
    static class GolfCartLauncherAimMath
    {
        /// <summary>Signed pitch of a direction in degrees; positive is upward.</summary>
        internal static float PitchDeg(Vector3 v) =>
            -Mathf.Atan2(v.y, new Vector2(v.x, v.z).magnitude) * Mathf.Rad2Deg;

        /// <summary>Compass yaw of a direction in degrees.</summary>
        internal static float YawDeg(Vector3 v) => Mathf.Atan2(v.x, v.z) * Mathf.Rad2Deg;

        /// <summary>Wraps an angle into (-180, 180].</summary>
        internal static float WrapAngleDeg(float deg)
        {
            float wrapped = deg % 360f;
            if (wrapped > 180f)
                wrapped -= 360f;
            else if (wrapped <= -180f)
                wrapped += 360f;
            return wrapped;
        }
    }
}
