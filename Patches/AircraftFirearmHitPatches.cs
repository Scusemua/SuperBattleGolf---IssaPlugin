using HarmonyLib;
using IssaPlugin.Items;
using Mirror;
using UnityEngine;

namespace IssaPlugin.Patches
{
    /// <summary>
    /// Enables firearms (elephant gun, sniper rifle, AK-47, dueling pistol) to
    /// shoot down large aircraft (Harrier, AC-130, Stealth Bomber).
    ///
    /// The primary firearm raycasts use GunHittablesMask and cannot reach aircraft
    /// colliders. These patches fire a secondary all-layers raycast on the same
    /// ray whenever a shot misses a player, invoking OnHit on any aircraft hit
    /// receiver found along the ray.
    ///
    /// The AK-47 is handled separately in AK47Item.DoShoot because its VFX call
    /// is rate-limited and would silently drop aircraft hits between VFX intervals.
    ///
    /// Each aircraft's FirearmsCanHit config toggle controls whether firearm hits
    /// count. Hit thresholds are shared with rockets via the existing
    /// HitsToDestroy/HitsToMayday configs.
    /// </summary>
    // ── Elephant gun / sniper rifle ──────────────────────────────────────────────

    [HarmonyPatch(typeof(VfxManager), "PlayElephantGunMissForAllClients")]
    static class ElephantGunAircraftHitPatch
    {
        static void Postfix(PlayerInventory shootingPlayer, Vector3 direction)
        {
            if (!NetworkServer.active || shootingPlayer == null)
                return;

            AircraftFirearmHitHelper.TryHitAircraftAlongRay(
                shootingPlayer.GetElephantGunBarrelEndPosition(),
                direction
            );
        }
    }

    // ── Dueling pistol ───────────────────────────────────────────────────────────

    [HarmonyPatch(typeof(VfxManager), "PlayDuelingPistolMissForAllClients")]
    static class DuelingPistolAircraftHitPatch
    {
        static void Postfix(PlayerInventory shootingPlayer, Vector3 direction)
        {
            if (!NetworkServer.active || shootingPlayer == null)
                return;

            AircraftFirearmHitHelper.TryHitAircraftAlongRay(
                shootingPlayer.GetDuelingPistolBarrelEndPosition(),
                direction
            );
        }
    }

    // ── Shared helper ────────────────────────────────────────────────────────────

    /// <summary>
    /// Fires an all-layers raycast and invokes OnHit on any aircraft hit receiver
    /// found along the ray. Called by the VFX miss patches above and directly from
    /// AK47Item.DoShoot (which rate-limits its own VFX call).
    /// </summary>
    public static class AircraftFirearmHitHelper
    {
        private const float MaxRange = 2000f;

        public static void TryHitAircraftAlongRay(Vector3 origin, Vector3 direction)
        {
            if (direction == Vector3.zero)
                return;

            // RaycastAll so we don't stop at the first collider — aircraft may be
            // behind terrain trigger volumes. QueryTriggerInteraction.Collide is
            // needed to reach the Stealth Bomber's trigger-sphere proxy collider.
            var hits = Physics.RaycastAll(
                origin,
                direction,
                MaxRange,
                Physics.AllLayers,
                QueryTriggerInteraction.Collide
            );

            // Dedup flags: invoke OnHit at most once per aircraft per shot.
            bool hitAC130 = false;
            bool hitHarrier = false;
            bool hitBomber = false;

            foreach (var hit in hits)
            {
                if (!hitAC130)
                {
                    var ac130 = hit.collider.GetComponentInParent<AC130HitReceiver>();
                    if (ac130 != null && ModConfig.AC130.FirearmsCanHit.Value)
                    {
                        hitAC130 = true;
                        IssaPluginPlugin.Log.LogInfo(
                            "[AC130] Firearm hit — notifying AC130HitReceiver."
                        );
                        ac130.OnHit?.Invoke();
                    }
                }

                if (!hitHarrier)
                {
                    var harrier = hit.collider.GetComponentInParent<HarrierHitReceiver>();
                    if (harrier != null && ModConfig.Harrier.FirearmsCanHit.Value)
                    {
                        hitHarrier = true;
                        harrier.LastHitWorldPos = hit.point;
                        IssaPluginPlugin.Log.LogInfo(
                            "[Harrier] Firearm hit — notifying HarrierHitReceiver."
                        );
                        harrier.OnHit?.Invoke();
                    }
                }

                if (!hitBomber)
                {
                    var bomber = hit.collider.GetComponentInParent<BomberProxyBehaviour>();
                    if (bomber != null && ModConfig.StealthBomber.FirearmsCanHit.Value)
                    {
                        hitBomber = true;
                        bomber.LastHitWorldPos = hit.point;
                        IssaPluginPlugin.Log.LogInfo(
                            "[Bomber] Firearm hit — notifying BomberProxyBehaviour."
                        );
                        bomber.OnHit?.Invoke();
                    }
                }
            }
        }
    }
}
