using System.Reflection;
using HarmonyLib;
using IssaPlugin.Items;
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
}
