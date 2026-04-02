using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using IssaPlugin.Items;
using Mirror;
using UnityEngine;

namespace IssaPlugin.Patches
{
    /// <summary>
    /// Component added to an OrbitalLaser GameObject (server-side) to track which
    /// bears have already been damaged so the explosion phase only hits each bear once.
    /// </summary>
    internal class BearOrbitalLaserTracker : MonoBehaviour
    {
        public readonly HashSet<GameObject> HitBears = new HashSet<GameObject>();
    }

    /// <summary>
    /// Makes bears take damage from the orbital laser.
    ///
    /// Bears have no Hittable component so the base game's laser explosion skips
    /// them entirely. This patch postfixes OrbitalLaser.OnBUpdate: when the laser
    /// enters the Exploding state it does an OverlapSphere around the laser's
    /// NetworktargetPosition and calls DealDamage on any bears found.
    ///
    /// A BearOrbitalLaserTracker component tracks which bears have been hit so
    /// the damage is only applied once per laser activation, regardless of how
    /// many frames the Exploding state lasts.
    ///
    /// Coexistence note: OrbitalLaserOnBUpdatePatch in OrbitalLaserPatches.cs
    /// already postfixes OnBUpdate for aircraft tracking. Harmony merges both
    /// postfixes on the same target without conflict.
    /// </summary>
    [HarmonyPatch(typeof(OrbitalLaser), "OnBUpdate")]
    static class BearOrbitalLaserHitPatch
    {
        private static readonly FieldInfo _stateField = AccessTools.Field(
            typeof(OrbitalLaser),
            "state"
        );

        static void Postfix(OrbitalLaser __instance)
        {
            if (!NetworkServer.active)
                return;

            var state = (OrbitalLaserState)_stateField.GetValue(__instance);
            if (state != OrbitalLaserState.Exploding)
                return;

            // Ensure we have a tracker to prevent double-hitting on subsequent frames.
            var tracker = __instance.GetComponent<BearOrbitalLaserTracker>();
            if (tracker == null)
                tracker = __instance.gameObject.AddComponent<BearOrbitalLaserTracker>();

            Vector3 explosionPos = __instance.NetworktargetPosition;
            float radius = GameManager.ItemSettings.OrbitalLaserExplosionMaxRange;

            var hits = Physics.OverlapSphere(
                explosionPos,
                radius,
                Physics.AllLayers,
                QueryTriggerInteraction.Collide
            );

            foreach (var col in hits)
            {
                var receiver = col.GetComponentInParent<BearHitReceiver>();
                if (receiver == null)
                    continue;

                if (!tracker.HitBears.Add(receiver.gameObject))
                    continue;

                float damage = ModConfig.Bear.DamageOrbitalLaser.Value;

                IssaPluginPlugin.Log.LogInfo(
                    $"[Bear] Orbital laser hit bear at {receiver.transform.position} "
                        + $"— {damage} damage."
                );

                NetworkServer.SendToAll(
                    new BearHitVfxMessage
                    {
                        HitPoint = receiver.transform.position + Vector3.up * 1f,
                        AttackerOrigin = explosionPos,
                    }
                );

                receiver.DealDamage(damage);
            }
        }
    }
}
