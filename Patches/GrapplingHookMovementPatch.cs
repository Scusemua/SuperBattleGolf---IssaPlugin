using System.Reflection;
using HarmonyLib;
using IssaPlugin.Items;
using UnityEngine;

namespace IssaPlugin.Patches
{
    /// Applies the rope after PlayerMovement has added gravity and input.
    /// Airborne horizontal velocity is replaced with the saved swing so the
    /// game's drag-cancelling acceleration cannot erase it or run away.
    /// Velocity and Position can be read directly; their setters are private.
    [HarmonyPatch]
    static class GrapplingHookMovementPatch
    {
        private static readonly MethodBase TargetMb;
        private static readonly PropertyInfo VelocityProp;
        private static readonly PropertyInfo PositionProp;
        private static readonly FieldInfo WishField;

        static GrapplingHookMovementPatch()
        {
            var movementType = typeof(PlayerMovement);
            TargetMb = AccessTools.Method(movementType, "FixedUpdate");
            VelocityProp = AccessTools.Property(movementType, "Velocity");
            PositionProp = AccessTools.Property(movementType, "Position");
            WishField = AccessTools.Field(movementType, "worldMoveVector3d");

            if (TargetMb == null || VelocityProp == null || PositionProp == null)
            {
                IssaPluginPlugin.Log.LogWarning(
                    "[Grapple] PlayerMovement swing members not found — swing patch skipped."
                );
                TargetMb = null;
                return;
            }

            if (WishField == null)
            {
                IssaPluginPlugin.Log.LogWarning(
                    "[Grapple] Movement wish vector not found — swing steering disabled."
                );
            }
        }

        static MethodBase TargetMethod() => TargetMb;

        static void Postfix(PlayerMovement __instance)
        {
            if (!__instance.isLocalPlayer || !GrapplingHookSession.OwnsSimulation)
                return;

            Vector3 wish = WishField != null ? (Vector3)WishField.GetValue(__instance) : Vector3.zero;
            if (
                !GrapplingHookSession.TryStep(
                    __instance,
                    __instance.Velocity,
                    wish,
                    out Vector3 velocity,
                    out Vector3 correctedPosition,
                    out bool correctPosition
                )
            )
                return;

            if (correctPosition)
                PositionProp.SetValue(__instance, correctedPosition);

            VelocityProp.SetValue(__instance, velocity);

            var body = __instance.PlayerInfo?.Rigidbody;
            if (body != null)
                __instance.NetworksyncedVelocity = body.linearVelocity;
        }
    }
}
