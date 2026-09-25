using System.Reflection;
using HarmonyLib;
using IssaPlugin.Items;
using UnityEngine;

namespace IssaPlugin.Patches
{
    /// <summary>
    /// GolfBall.UpdatePhysics forces <c>isKinematic = false</c> (and re-enables the
    /// sphere collider) every tick for any visible, non-OOB ball. The Glove hold
    /// wants the opposite — kinematic + non-hittable — so without these patches the
    /// game and the glove fight each frame: LateUpdate sets kinematic, FixedUpdate
    /// clears it, then ApplyGravity / ApplyAirDamping write linearVelocity and spam
    /// "Setting linear velocity of a kinematic body is not supported."
    /// </summary>
    [HarmonyPatch(typeof(GolfBall), "UpdatePhysics")]
    static class GloveBallUpdatePhysicsPatch
    {
        private static readonly FieldInfo ColliderField = AccessTools.Field(
            typeof(GolfBall),
            "collider"
        );

        static void Postfix(GolfBall __instance)
        {
            if (!GloveNetworkBridge.IsBallHeldByGlove(__instance))
                return;

            var rb = __instance.Rigidbody ?? __instance.AsEntity?.Rigidbody;
            if (rb != null && !rb.isKinematic)
                rb.isKinematic = true;

            // UpdatePhysics just re-enabled the main sphere — keep it off while held.
            if (ColliderField?.GetValue(__instance) is Collider col && col.enabled)
                col.enabled = false;
        }
    }

    /// <summary>
    /// Skip manual gravity integration while the Glove is carrying this ball.
    /// </summary>
    [HarmonyPatch(typeof(GolfBall), "ApplyGravity")]
    static class GloveBallApplyGravityPatch
    {
        static bool Prefix(GolfBall __instance) =>
            !GloveNetworkBridge.IsBallHeldByGlove(__instance);
    }

    /// <summary>
    /// Skip air/ground damping velocity writes while the Glove is carrying this ball.
    /// </summary>
    [HarmonyPatch(typeof(GolfBall), "ApplyLinearDamping")]
    static class GloveBallApplyLinearDampingPatch
    {
        static bool Prefix(GolfBall __instance) =>
            !GloveNetworkBridge.IsBallHeldByGlove(__instance);
    }
}
