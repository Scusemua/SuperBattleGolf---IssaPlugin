using System.Linq;
using System.Reflection;
using HarmonyLib;
using IssaPlugin.Items;
using UnityEngine;

namespace IssaPlugin.Patches
{
    [HarmonyPatch(typeof(Rocket), "ServerExplode")]
    class Patch_Rocket_ServerExplode
    {
        static void Postfix(Rocket __instance, Vector3 worldPosition)
        {
            var hits = Physics.OverlapSphere(worldPosition, 5f);

            foreach (var hit in hits)
            {
                var ac130HitReceiver = hit.GetComponentInParent<AC130HitReceiver>();
                if (ac130HitReceiver != null)
                {
                    ac130HitReceiver.OnHit();
                }

                var stealthBomberProxy = hit.GetComponentInParent<BomberProxyBehaviour>();
                if (stealthBomberProxy != null)
                {
                    stealthBomberProxy.LastHitWorldPos = worldPosition;
                    stealthBomberProxy.OnHit();
                }
            }
        }
    }

    [HarmonyPatch(typeof(Rocket), "OnFixedBUpdate")]
    static class RocketFixedBUpdatePatch
    {
        private static readonly FieldInfo DistanceField = AccessTools.Field(
            typeof(Rocket),
            "distanceTravelled"
        );

        static void Prefix(Rocket __instance)
        {
            if (!PredatorMissileItem.ActiveMissileRockets.Contains(__instance))
                return;

            DistanceField?.SetValue(__instance, 0f);
        }
    }
}
