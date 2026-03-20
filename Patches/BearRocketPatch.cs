using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using IssaPlugin.Items;
using Mirror;
using UnityEngine;

namespace IssaPlugin.Patches
{
    /// <summary>
    /// Extends the Rocket.ServerExplode pipeline to notify <see cref="BearHitReceiver"/>
    /// when a rocket explodes near a bear.
    ///
    /// WHY A SEPARATE PATCH CLASS:
    /// The existing Patch_Rocket_ServerExplode in RocketPatches.cs already has a Postfix
    /// on Rocket.ServerExplode. Harmony merges multiple postfixes on the same target —
    /// both will run. Keeping bear logic in its own class avoids touching RocketPatches.cs
    /// and makes the bear feature self-contained.
    ///
    /// ATTACKER IDENTIFICATION:
    /// To let the bear's aggro table know which player fired the killing rocket, we
    /// grab the rocket's launcher PlayerInfo in a Prefix (while the rocket is still
    /// alive) and store it in BearExplosionAttackerContext. The Postfix reads it back.
    /// The field name is found by trying several common decompiled names with reflection;
    /// if none match, attacker is null and the bear simply keeps its existing aggro target.
    ///
    /// COEXISTENCE WITH Patch_Rocket_ServerExplode:
    /// Both patches operate on the same target. They do not interfere because:
    ///   - Patch_Rocket_ServerExplode handles AC130HitReceiver, BomberProxyBehaviour,
    ///     DonutHitReceiver (existing custom hittables).
    ///   - BearRocketExplosionPatch handles BearHitReceiver (new).
    /// There is no shared mutable state between them.
    /// </summary>
    [HarmonyPatch(typeof(Rocket), "ServerExplode")]
    static class BearRocketExplosionPatch
    {
        // ── Reusable dedup set — cleared per explosion, never reallocated ─────
        private static readonly HashSet<GameObject> _notifiedBears = new HashSet<GameObject>();

        // ── Cached reflection fields ──────────────────────────────────────────

        /// <summary>
        /// Cached FieldInfo for the Rocket's launcher PlayerInfo.
        /// Populated on first access; null if the field cannot be found under
        /// any of the candidate names.
        /// </summary>
        private static FieldInfo _launcherField;
        private static bool _launcherFieldSearched;

        private static readonly string[] LauncherFieldCandidates =
        {
            "launcher", // most likely from decompiled source
            "_launcher",
            "launcherInfo",
            "playerInfo",
            "_playerInfo",
        };

        // ── Prefix — capture attacker before the rocket is destroyed ──────────

        static void Prefix(Rocket __instance)
        {
            if (!NetworkServer.active)
                return;

            // Resolve the launcher field once
            if (!_launcherFieldSearched)
            {
                _launcherFieldSearched = true;
                var flags = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance;

                foreach (var name in LauncherFieldCandidates)
                {
                    var fi = typeof(Rocket).GetField(name, flags);
                    if (
                        fi != null
                        && (
                            fi.FieldType == typeof(PlayerInfo)
                            || fi.FieldType.IsSubclassOf(typeof(PlayerInfo))
                        )
                    )
                    {
                        _launcherField = fi;
                        IssaPluginPlugin.Log.LogInfo(
                            $"[BearRocketPatch] Found launcher field: '{name}'"
                        );
                        break;
                    }
                }

                if (_launcherField == null)
                    IssaPluginPlugin.Log.LogWarning(
                        "[BearRocketPatch] Could not find Rocket launcher field. "
                            + "Bear aggro will not track rocket attackers. "
                            + "Check decompiled Rocket source and update LauncherFieldCandidates."
                    );
            }

            var launcher = _launcherField?.GetValue(__instance) as PlayerInfo;
            BearExplosionAttackerContext.CurrentAttacker = launcher;
        }

        // ── Postfix — hit detection ───────────────────────────────────────────

        static void Postfix(Rocket __instance, Vector3 worldPosition)
        {
            if (!NetworkServer.active)
                return;

            try
            {
                // Overlap sphere radius matches the existing rocket explosion patch.
                var hits = Physics.OverlapSphere(
                    worldPosition,
                    8f,
                    Physics.AllLayers,
                    QueryTriggerInteraction.Collide
                );

                // De-duplicate by bear root GameObject so compound colliders
                // (body + head + paw) don't trigger multiple hits for one explosion.
                _notifiedBears.Clear();

                foreach (var col in hits)
                {
                    var receiver = col.GetComponentInParent<BearHitReceiver>();
                    if (receiver == null)
                        continue;

                    if (!_notifiedBears.Add(receiver.gameObject))
                        continue;

                    IssaPluginPlugin.Log.LogInfo(
                        $"[Bear] Rocket exploded near bear at {worldPosition} — hit registered."
                    );
                    receiver.OnHit?.Invoke();
                }
            }
            finally
            {
                // Always clear the attacker context whether or not an exception occurred
                BearExplosionAttackerContext.CurrentAttacker = null;
            }
        }
    }
}
