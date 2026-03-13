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

    // [HarmonyPatch]
    // static class Patch_Rocket_CheckCollision
    // {
    //     static MethodInfo TargetMethod() =>
    //         typeof(Rocket)
    //             .GetMethods(BindingFlags.NonPublic | BindingFlags.Instance)
    //             .FirstOrDefault(m => m.Name.Contains("CheckCollision"));

    //     static bool Prefix(Rocket __instance, out Vector3 explosionPosition)
    //     {
    //         var hits = Physics.OverlapSphere(worldPosition, 5f);
    //         bool collisionDetected = false;

    //         foreach (var hit in hits)
    //         {
    //             var ac130HitReceiver = hit.GetComponentInParent<AC130HitReceiver>();
    //             if (ac130HitReceiver != null)
    //             {
    //                 ac130HitReceiver.OnHit();
    //                 collisionDetected = true;
    //             }

    //             var stealthBomberProxy = hit.GetComponentInParent<BomberProxyBehaviour>();
    //             if (stealthBomberProxy != null)
    //             {
    //                 stealthBomberProxy.LastHitWorldPos = worldPosition;
    //                 stealthBomberProxy.OnHit();
    //                 collisionDetected = true;
    //             }
    //         }

    //         // If we detected a collision, then just skip the real CheckCollision method.
    //         return !collisionDetected;
    //     }
    // }
}
