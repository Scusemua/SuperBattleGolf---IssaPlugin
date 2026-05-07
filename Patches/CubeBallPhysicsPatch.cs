using System.Reflection;
using HarmonyLib;
using IssaPlugin.Items;
using Mirror;
using UnityEngine;

namespace IssaPlugin.Patches
{
    /// Harmony postfix on GolfBall.UpdateFrictionMode.
    ///
    /// The game calls UpdateFrictionMode whenever the ball transitions between air
    /// and ground states, immediately setting rigidbody.angularDamping from
    /// GolfBallSettings. This undoes our angular damping override every time.
    ///
    /// This postfix re-applies the configurable damping value after the game's
    /// reset, but only when the ball has an active CubeBallState component.
    [HarmonyPatch]
    static class CubeBallPhysicsPatch
    {
        private static readonly MethodBase TargetMb;

        static CubeBallPhysicsPatch()
        {
            var type = AccessTools.TypeByName("GolfBall");
            if (type == null)
            {
                IssaPluginPlugin.Log.LogWarning(
                    "[CubeBall] GolfBall type not found — angular damping patch skipped."
                );
                return;
            }

            TargetMb = AccessTools.Method(type, "UpdateFrictionMode");
            if (TargetMb == null)
                IssaPluginPlugin.Log.LogWarning(
                    "[CubeBall] GolfBall.UpdateFrictionMode not found — angular damping patch skipped."
                );
        }

        static MethodBase TargetMethod() => TargetMb;

        static void Postfix(Component __instance)
        {
            if (__instance.GetComponent<CubeBallState>() == null)
                return;

            var rb = __instance.GetComponent<Rigidbody>();
            if (rb != null)
                rb.angularDamping = ModConfig.CubeBall.PhysicsAngularDamping.Value;
        }
    }

    /// Harmony postfix on Hittable.HitWithGolfSwingInternal.
    ///
    /// When a cubed ball is hit, the flat face in contact with the ground means
    /// ground friction produces zero net torque — the cube slides instead of
    /// rolling.  This postfix injects an angular impulse about the transverse axis
    /// (perpendicular to velocity, horizontal) so the cube immediately tumbles.
    [HarmonyPatch]
    static class CubeBallHitSpinPatch
    {
        static MethodBase TargetMethod() =>
            AccessTools.Method(typeof(Hittable), "HitWithGolfSwingInternal");

        static void Postfix(Hittable __instance)
        {
            if (!NetworkServer.active)
                return;

            if (!__instance.AsEntity.IsGolfBall)
                return;

            if (__instance.GetComponent<CubeBallState>() == null)
                return;

            float spinFactor = ModConfig.CubeBall.PhysicsHitSpinFactor.Value;
            if (spinFactor <= 0f)
                return;

            var rb = __instance.GetComponent<Rigidbody>();
            if (rb == null)
                return;

            float speed = rb.linearVelocity.magnitude;
            if (speed < 0.01f)
                return;

            // Axis perpendicular to velocity and gravity → produces forward rolling spin.
            Vector3 rollAxis = Vector3.Cross(rb.linearVelocity.normalized, Vector3.up);
            if (rollAxis.sqrMagnitude < 0.01f)
                rollAxis = Vector3.right;

            // Primary spin + 30% random chaos to prevent perfectly predictable tumbling.
            rb.angularVelocity +=
                rollAxis * (speed * spinFactor)
                + Random.insideUnitSphere * (speed * spinFactor * 0.3f);
        }
    }
}
