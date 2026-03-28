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
    /// </summary>
    [HarmonyPatch(typeof(Entity), "GetNetworkedPointVelocity")]
    static class EntityGetNetworkedPointVelocityPatch
    {
        static bool Prefix(Entity __instance, Vector3 worldPoint, ref Vector3 __result)
        {
            if (__instance.Rigidbody != null && __instance.NetworkRigidbody == null)
            {
                __result = __instance.Rigidbody.GetPointVelocity(worldPoint);
                return false;
            }
            return true;
        }
    }
}
