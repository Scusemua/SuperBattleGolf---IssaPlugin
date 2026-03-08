using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using IssaPlugin.Items;
using UnityEngine;

namespace IssaPlugin.Patches
{
    /// <summary>
    /// Adds bonus explosion effects (bigger VFX, extra knockback, extended radius)
    /// for custom-item rockets that have a scale != 1 registered in ExplosionScaler.
    ///
    /// The normal game explosion runs first at its default radius and force.
    /// The Postfix then layers on additional effects proportional to the scale.
    /// </summary>
    [HarmonyPatch]
    static class ServerExplodeScalePatch
    {
        static MethodBase TargetMethod() =>
            AccessTools.Method(typeof(Rocket), "ServerExplode");

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

            // --- Bonus VFX: spawn a second, larger explosion particle ---
            if (scale > 1f)
            {
                VfxManager.PlayPooledVfxLocalOnly(
                    VfxType.RocketLauncherRocketExplosion,
                    worldPosition,
                    Quaternion.identity,
                    Vector3.one * scale
                );

                CameraModuleController.Shake(
                    GameManager.CameraGameplaySettings.RocketExplosionScreenshakeSettings,
                    worldPosition
                );
            }

            // --- Bonus knockback + extended radius hits ---
            if (scale > 1f)
            {
                int layerMask = GameManager.LayerSettings.RocketHittablesMask;
                var colliders = Physics.OverlapSphere(
                    worldPosition,
                    scaledRange,
                    layerMask,
                    QueryTriggerInteraction.Ignore
                );

                float bonusForce = (scale - 1f) * 25f;
                var processed = new HashSet<Rigidbody>();

                foreach (var col in colliders)
                {
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
}
