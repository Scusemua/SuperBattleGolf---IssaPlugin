using System.Reflection;
using HarmonyLib;
using IssaPlugin.Items;
using Mirror;
using UnityEngine;

namespace IssaPlugin.Patches
{
    /// <summary>
    /// Suppresses wind on the immune player's golf ball during an active Wind Storm.
    ///
    /// GolfBall.ApplyLinearDamping (private) calls:
    ///   this.AsEntity.AsHittable.ApplyAirDamping(..., this.canBeAffectedByWind)
    ///
    /// By forcing canBeAffectedByWind = false before that call executes, wind is never
    /// applied to the ball.  The game sets it back to true on the next hit, but our prefix
    /// will override it again on the very next physics frame — keeping the ball wind-free
    /// for the entire storm duration.
    /// </summary>
    [HarmonyPatch]
    static class WindStormWindImmunityPatch
    {
        private static readonly FieldInfo CanBeAffectedByWindField = AccessTools.Field(
            typeof(GolfBall),
            "canBeAffectedByWind"
        );

        [HarmonyTargetMethod]
        static MethodBase TargetMethod() =>
            AccessTools.Method(typeof(GolfBall), "ApplyLinearDamping");

        [HarmonyPrefix]
        static void Prefix(GolfBall __instance)
        {
            if (WindStormNetworkBridge.WindImmuneNetIds.Count == 0)
                return;

            var owner = __instance.Owner;
            if (owner == null)
                return;

            var ownerIdentity = owner.GetComponent<NetworkIdentity>();
            if (ownerIdentity == null)
                return;

            if (WindStormNetworkBridge.WindImmuneNetIds.Contains(ownerIdentity.netId))
                CanBeAffectedByWindField.SetValue(__instance, false);
        }
    }
}
