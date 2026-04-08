using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace IssaPlugin.Patches
{
    /// <summary>
    /// Guards against a NullReferenceException in Entity.GetNetworkedPointVelocity.
    ///
    /// The base game bug: when an Entity has a Rigidbody but no NetworkRigidbodyUnreliable
    /// component (NetworkRigidbody == null) and IsSimulatingRigidbody() returns false,
    /// GetNetworkedPointVelocity falls through to access NetworkRigidbody.velocity —
    /// throwing NullReferenceException.
    ///
    /// This is triggered on "knocked-out" players after large explosions (e.g. Nuke):
    ///   Entity.GetNetworkedPointVelocity
    ///   PlayerMovement.HandleKnockedOutCollision
    ///   PlayerMovement.OnCollisionEnter
    ///
    /// Fix: when NetworkRigidbody is null but Rigidbody exists, fall back to
    /// Rigidbody.GetPointVelocity — the same value the simulating-authority path returns.
    ///
    /// Note: NetworkRigidbody's return type (NetworkRigidbodyUnreliable) lives in
    /// Mirror.Components.dll which is not shipped with the game — access via reflection
    /// to avoid a compile-time assembly reference.
    /// </summary>
    [HarmonyPatch(typeof(Entity), "GetNetworkedPointVelocity")]
    static class EntityGetNetworkedPointVelocityPatch
    {
        private static readonly PropertyInfo NetworkRigidbodyProp = AccessTools.Property(
            typeof(Entity),
            "NetworkRigidbody"
        );

        static bool Prefix(Entity __instance, Vector3 worldPoint, ref Vector3 __result)
        {
            if (__instance.Rigidbody != null && NetworkRigidbodyProp?.GetValue(__instance) == null)
            {
                __result = __instance.Rigidbody.GetPointVelocity(worldPoint);
                return false;
            }
            return true;
        }
    }
}
