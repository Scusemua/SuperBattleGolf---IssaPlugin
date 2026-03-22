using System.Linq;
using System.Reflection;
using HarmonyLib;
using IssaPlugin.Items;
using UnityEngine;

namespace IssaPlugin.Patches
{
    [HarmonyPatch(typeof(Rocket), "Start")]
    static class RocketStartPatch
    {
        static void Postfix(Rocket __instance)
        {
            if (!PredatorMissileItem.ActiveMissileRockets.Contains(__instance))
                return;

            var entity = __instance.GetComponent<Entity>();
            if (entity != null && entity.HasRigidbody)
            {
                entity.Rigidbody.linearVelocity =
                    Vector3.down * Configuration.MissileFallSpeed.Value;
            }
        }
    }

    [HarmonyPatch(typeof(Rocket), "ServerExplode")]
    class Patch_Rocket_ServerExplode
    {
        static void Postfix(Rocket __instance, Vector3 worldPosition)
        {
            var hits = Physics.OverlapSphere(
                worldPosition,
                8f,
                Physics.AllLayers,
                QueryTriggerInteraction.Collide
            );

            // Remove returns true if the ID was present, cleaning up the set at the
            // same time. Bombing-run rockets must not count as hits on their own proxy.
            bool isBomberDrop = StealthBomberItem.ActiveBomberDropRocketIds.Remove(
                __instance.GetInstanceID()
            );

            foreach (var hit in hits)
            {
                var ac130HitReceiver = hit.GetComponentInParent<AC130HitReceiver>();
                if (ac130HitReceiver != null)
                {
                    IssaPluginPlugin.Log.LogInfo(
                        $"[AC130] Rocket exploded within range — notifying AC130HitReceiver."
                    );
                    ac130HitReceiver.OnHit?.Invoke();
                }

                if (!isBomberDrop)
                {
                    var stealthBomberProxy = hit.GetComponentInParent<BomberProxyBehaviour>();
                    if (stealthBomberProxy != null)
                    {
                        stealthBomberProxy.LastHitWorldPos = worldPosition;
                        stealthBomberProxy.OnHit();
                    }
                }

                var donutHitReceiver = hit.GetComponentInParent<DonutHitReceiver>();
                if (donutHitReceiver != null)
                {
                    donutHitReceiver.OnHit();
                }

                var wallChunk = hit.GetComponentInParent<PlaceableWallDestructionBehaviour>();
                if (wallChunk != null)
                {
                    wallChunk.ApplyExplosionDamage(worldPosition, 8f);
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

        static bool Prefix(Rocket __instance)
        {
            // Javelin rockets: fully suppress OnFixedBUpdate so JavelinRocketBehaviour
            // has exclusive control over velocity. Return false = skip the original.
            if (JavelinItem.ActiveJavelinRockets.Contains(__instance))
                return false;

            // Predator Missile rockets: zero distanceTravelled so the range-limit
            // never fires, but allow the rest of OnFixedBUpdate to run normally
            // (the client overrides velocity every frame via MissileSetVelocityMessage).
            if (PredatorMissileItem.ActiveMissileRockets.Contains(__instance))
                DistanceField?.SetValue(__instance, 0f);

            return true;
        }
    }

    [HarmonyPatch(typeof(Rocket), "OnLateBUpdate")]
    static class RocketLateBUpdatePatch
    {
        static bool Prefix(Rocket __instance)
        {
            // Javelin rockets: suppress OnLateBUpdate so it never calls
            // LookRotation(linearVelocity) — which writes Quaternion.identity to the
            // transform when velocity is zero (Turning phase), undoing our rotation work.
            return !JavelinItem.ActiveJavelinRockets.Contains(__instance);
        }
    }
}
