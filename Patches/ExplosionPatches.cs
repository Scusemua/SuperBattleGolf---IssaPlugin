using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using IssaPlugin.Items;
using Mirror;
using UnityEngine;

namespace IssaPlugin.Patches
{
    /// Adds bonus explosion effects (bigger VFX, extra knockback, extended radius)
    /// for custom-item rockets that have a scale != 1 registered in ExplosionScaler.
    ///
    /// The normal game explosion runs first at its default radius and force.
    /// The Postfix then layers on additional effects proportional to the scale.
    [HarmonyPatch]
    static class ServerExplodeScalePatch
    {
        static readonly Collider[] overlappingColliderBuffer = new Collider[100];

        // Reused across explosions to avoid allocating a set per detonation. Explosions
        // arrive in bursts (a stealth bomber run drops one every 0.75s, AC130 fires
        // every 0.35s), so a fresh HashSet each time is steady GC pressure during
        // exactly the moments the frame budget is tightest. ServerExplode is
        // single-threaded on the server, so a shared instance is safe.
        static readonly HashSet<Rigidbody> processedBuffer = new HashSet<Rigidbody>();

        static MethodBase TargetMethod() => AccessTools.Method(typeof(Rocket), "ServerExplode");

        static void Postfix(Rocket __instance, Vector3 worldPosition)
        {
            float scale = ExplosionScaler.GetScale(__instance);
            ExplosionScaler.Unregister(__instance);

            if (scale <= 0f || Mathf.Approximately(scale, 1f))
                return;

            float baseRange = GameManager.ItemSettings.RocketExplosionRange;
            float scaledRange = baseRange * scale;

            IssaPluginPlugin.Log.LogInfo(
                $"[Explosion] Custom rocket scale={scale:F2}, "
                    + $"baseRange={baseRange:F1}, scaledRange={scaledRange:F1}"
            );

            // Broadcast the bigger VFX + screen-shake to all clients.
            // PlayPooledVfxLocalOnly must be called on the client — calling it
            // directly here (server-side) only renders on the host and never
            // reaches remote clients.
            NetworkServer.SendToAll(
                new ScaledExplosionVfxMessage { Position = worldPosition, Scale = scale }
            );

            // Bonus knockback + extended radius hits (server-authoritative).
            int layerMask = GameManager.LayerSettings.RocketHittablesMask;
            int hitCount = Physics.OverlapSphereNonAlloc(
                worldPosition,
                scaledRange,
                overlappingColliderBuffer,
                layerMask,
                QueryTriggerInteraction.Ignore
            );

            float bonusForce = (scale - 1f) * 25f;
            var processed = processedBuffer;
            processed.Clear();

            for (int i = 0; i < hitCount; i++)
            {
                var col = overlappingColliderBuffer[i];
                var rb = col.GetComponentInParent<Rigidbody>();
                if (rb != null && processed.Add(rb))
                {
                    rb.AddExplosionForce(
                        bonusForce,
                        worldPosition,
                        scaledRange,
                        0.3f,
                        ForceMode.VelocityChange
                    );
                }
            }

            IssaPluginPlugin.Log.LogInfo(
                $"[Explosion] Bonus force={bonusForce:F1} applied to "
                    + $"{processed.Count} rigidbodies within {scaledRange:F1}m"
            );
        }
    }
}
