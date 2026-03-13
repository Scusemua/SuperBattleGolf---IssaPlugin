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
                    continue;
                }

                var stealthBomberItem = hit.GetComponentInParent<BomberProxyBehaviour>();
                if (stealthBomberItem != null)
                {
                    stealthBomberItem.OnHit();
                    continue;
                }
            }
        }
    }
}
