using System.Linq;
using System.Reflection;
using HarmonyLib;
using IssaPlugin.Items;
using Mirror;
using UnityEngine;

namespace IssaPlugin.Patches
{
    // ====================================================================
    //  Helper component attached to the OrbitalLaser GameObject (server)
    //  when the laser is targeting an aircraft instead of a player.
    // ====================================================================

    internal class OrbitalLaserAircraftTracker : MonoBehaviour
    {
        /// <summary>The AC130 gunship or bomber proxy Transform to follow.</summary>
        public Transform AircraftTransform;

        /// <summary>Fired once on the server when the explosion hits the aircraft.</summary>
        public System.Action OnHit;

        /// <summary>Prevents the callback from firing more than once.</summary>
        public bool Triggered;
    }

    /// <summary>
    /// Non-patch static helpers for orbital laser aircraft targeting.
    /// Kept outside [HarmonyPatch] classes so the Harmony analyzer does not
    /// flag their parameters as unused patch injections.
    /// </summary>
    internal static class OrbitalLaserAircraftHelpers
    {
        public static float XZDist(Vector3 p1, Vector3 p2) =>
            new Vector2(p1.x - p2.x, p1.z - p2.z).magnitude;
    }

    // ====================================================================
    //  Patch 1: OrbitalLaserManager.GetTarget() — client-side postfix
    //
    //  When an AC130 or stealth bomber is active, always override the return
    //  value to (null Hittable, aircraftPosition) so the laser fires in
    //  stationary mode tracking the aircraft's ground projection. Aircraft
    //  always take priority over players — we don't compare distances because
    //  the player is typically near the hole while the aircraft isn't, which
    //  would cause the player to win the comparison even when an aircraft is
    //  the intended target.
    //
    //  AC130GunshipMarker and BomberMarker are added on all clients via RPC,
    //  so FindFirstObjectByType works correctly on remote clients.
    // ====================================================================

    [HarmonyPatch]
    static class OrbitalLaserGetTargetPatch
    {
        static MethodBase TargetMethod() =>
            AccessTools.Method(typeof(OrbitalLaserManager), "GetTarget");

        static void Postfix(ref Hittable __result, ref Vector3 fallbackPosition)
        {
            // Aircraft always take priority over players — no distance comparison.
            // (If we compared XZ distance to the hole the player would often win
            // because they're near the hole while the AC130 is elsewhere.)
            Vector3 holePos = GolfHoleManager.MainHole.transform.position;
            Transform bestAircraft = null;
            float bestDist = float.MaxValue;

            var gunshipMarker = Object.FindFirstObjectByType<AC130GunshipMarker>();
            if (gunshipMarker != null)
            {
                float d = OrbitalLaserAircraftHelpers.XZDist(
                    gunshipMarker.transform.position,
                    holePos
                );
                if (d < bestDist)
                {
                    bestDist = d;
                    bestAircraft = gunshipMarker.transform;
                }
            }

            var bomberMarker = Object.FindFirstObjectByType<BomberMarker>();
            if (bomberMarker != null)
            {
                float d = OrbitalLaserAircraftHelpers.XZDist(
                    bomberMarker.transform.position,
                    holePos
                );
                if (d < bestDist)
                {
                    bestDist = d;
                    bestAircraft = bomberMarker.transform;
                }
            }

            var ufoMarker = Object.FindFirstObjectByType<UFOMarker>();
            if (ufoMarker != null)
            {
                float d = OrbitalLaserAircraftHelpers.XZDist(ufoMarker.transform.position, holePos);
                if (d < bestDist)
                {
                    bestDist = d;
                    bestAircraft = ufoMarker.transform;
                }
            }

            if (bestAircraft == null)
                return;

            // Override: null Hittable puts the laser in stationary mode.
            // fallbackPosition is set to the aircraft's current world position;
            // OrbitalLaser.SnapHeight will project it to the terrain below.
            __result = null;
            fallbackPosition = bestAircraft.position;

            IssaPluginPlugin.Log.LogInfo(
                $"[OrbitalLaser] Targeting aircraft at {bestAircraft.position}."
            );
        }
    }

    // ====================================================================
    //  Patch 1b: PlayerInventory — compiler-generated UpdateOrbitalLaserLockOnTarget
    //
    //  The base game's lock-on update calls GetTarget() and, when the result
    //  is null, clears the indicator. Our GetTarget patch returns null for
    //  aircraft targets, so no reticle would appear. This prefix intercepts
    //  the method: when an aircraft with a LockOnTarget component is present
    //  we feed it directly to SetLockOnTarget and skip the base method so the
    //  standard lock-on reticle tracks the aircraft just like it would a player.
    // ====================================================================

    [HarmonyPatch]
    static class OrbitalLaserLockOnIndicatorPatch
    {
        static MethodInfo TargetMethod() =>
            typeof(PlayerInventory)
                .GetMethods(BindingFlags.NonPublic | BindingFlags.Instance)
                .FirstOrDefault(m => m.Name.Contains("UpdateOrbitalLaserLockOnTarget"));

        private static readonly MethodInfo _setLockOnTarget = AccessTools.Method(
            typeof(PlayerInventory),
            "SetLockOnTarget"
        );

        static bool Prefix(PlayerInventory __instance)
        {
            // Mirror the base method's early-exit when the item is being used.
            if (__instance.IsUsingItemAtAll)
                return true;

            // Find the aircraft LockOnTarget closest to the hole.
            Vector3 holePos = GolfHoleManager.MainHole.transform.position;
            LockOnTarget bestLockOn = null;
            float bestDist = float.MaxValue;

            var gunshipMarker = Object.FindFirstObjectByType<AC130GunshipMarker>();
            if (gunshipMarker != null)
            {
                var lot = gunshipMarker.GetComponent<LockOnTarget>();
                if (lot != null)
                {
                    float d = OrbitalLaserAircraftHelpers.XZDist(
                        gunshipMarker.transform.position,
                        holePos
                    );
                    if (d < bestDist)
                    {
                        bestDist = d;
                        bestLockOn = lot;
                    }
                }
            }

            var bomberMarker = Object.FindFirstObjectByType<BomberMarker>();
            if (bomberMarker != null)
            {
                var lot = bomberMarker.GetComponent<LockOnTarget>();
                if (lot != null)
                {
                    float d = OrbitalLaserAircraftHelpers.XZDist(
                        bomberMarker.transform.position,
                        holePos
                    );
                    if (d < bestDist)
                    {
                        bestDist = d;
                        bestLockOn = lot;
                    }
                }
            }

            var ufoMarker = Object.FindFirstObjectByType<UFOMarker>();
            if (ufoMarker != null)
            {
                var lot = ufoMarker.GetComponent<LockOnTarget>();
                if (lot != null)
                {
                    float d = OrbitalLaserAircraftHelpers.XZDist(
                        ufoMarker.transform.position,
                        holePos
                    );
                    if (d < bestDist)
                    {
                        bestDist = d;
                        bestLockOn = lot;
                    }
                }
            }

            if (bestLockOn == null)
                return true; // no aircraft — let base method run normally

            // Point the lock-on reticle at the aircraft and skip the base method.
            _setLockOnTarget.Invoke(__instance, new object[] { bestLockOn });
            return false;
        }
    }

    // ====================================================================
    //  Patch 2: OrbitalLaser.ServerActivate() — server-side postfix
    //
    //  When the laser activates with a null Hittable target and an active
    //  aircraft is near the fallback position, attach an
    //  OrbitalLaserAircraftTracker to enable per-frame tracking and
    //  automatic shot-down detection during the explosion phase.
    //
    //  Uses server-side references (AC130NetworkBridge.ActiveGunship and
    //  BomberProxyBehaviour) rather than the client-side markers.
    // ====================================================================

    [HarmonyPatch(typeof(OrbitalLaser), "ServerActivate")]
    static class OrbitalLaserServerActivatePatch
    {
        static void Postfix(OrbitalLaser __instance, Hittable target, Vector3 fallbackWorldPosition)
        {
            if (!NetworkServer.active || target != null)
                return;

            // Pick the aircraft closest to fallbackWorldPosition (the position the
            // client reported when firing). We use FindFirstObjectByType here —
            // not AC130NetworkBridge.ActiveGunship — because ActiveGunship becomes
            // null once the session ends (fly-out), even though the gunship
            // GameObject still exists and the laser should still track it.
            Transform bestAircraft = null;
            System.Action onHit = null;
            float bestDist = float.MaxValue;

            // AC130 gunship — AC130GunshipMarker is added server-side in ServerSpawnGunship.
            var gunshipMarker = Object.FindFirstObjectByType<AC130GunshipMarker>();
            if (gunshipMarker != null)
            {
                float d = OrbitalLaserAircraftHelpers.XZDist(
                    gunshipMarker.transform.position,
                    fallbackWorldPosition
                );
                if (d < bestDist)
                {
                    bestDist = d;
                    bestAircraft = gunshipMarker.transform;
                    var hitReceiver = gunshipMarker.GetComponent<AC130HitReceiver>();
                    onHit = () => hitReceiver?.OnHit();
                }
            }

            // Bomber proxy
            var proxy = Object.FindFirstObjectByType<BomberProxyBehaviour>();
            if (proxy != null)
            {
                float d = OrbitalLaserAircraftHelpers.XZDist(
                    proxy.transform.position,
                    fallbackWorldPosition
                );
                if (d < bestDist)
                {
                    bestDist = d;
                    bestAircraft = proxy.transform;
                    onHit = () => proxy.OnHit();
                }
            }

            // UFO
            var ufoHitReceiver = Object.FindFirstObjectByType<UFOHitReceiver>();
            if (ufoHitReceiver != null)
            {
                float d = OrbitalLaserAircraftHelpers.XZDist(
                    ufoHitReceiver.transform.position,
                    fallbackWorldPosition
                );
                if (d < bestDist)
                {
                    bestDist = d;
                    bestAircraft = ufoHitReceiver.transform;
                    onHit = () => ufoHitReceiver.OnHit();
                }
            }

            if (bestAircraft == null)
            {
                IssaPluginPlugin.Log.LogWarning(
                    "[OrbitalLaser] ServerActivate: target is null but no aircraft found on server."
                );
                return;
            }

            var tracker = __instance.gameObject.AddComponent<OrbitalLaserAircraftTracker>();
            tracker.AircraftTransform = bestAircraft;
            tracker.OnHit = onHit;

            // Snap NetworktargetPosition to the server-authoritative aircraft position
            // so the beam starts at the correct location even if there was slight
            // client/server position drift during the activation delay.
            __instance.NetworktargetPosition = bestAircraft.position;

            IssaPluginPlugin.Log.LogInfo(
                $"[OrbitalLaser] Tracker attached to {bestAircraft.name} "
                    + $"at {bestAircraft.position} (dist from client pos: {bestDist:F1}m)."
            );
        }
    }

    // ====================================================================
    //  Patch 3: OrbitalLaser.OnBUpdate() — server-side postfix
    //
    //  (a) Anticipation phase: update NetworktargetPosition each frame so
    //      the laser indicator visually tracks the aircraft's current XZ
    //      position. SnapHeight (called by UpdatePosition) projects this
    //      to the terrain below the aircraft.
    //
    //  (b) Exploding phase: if the aircraft's XZ position is within the
    //      explosion radius of the laser's ground position, fire the
    //      shot-down callback once.
    // ====================================================================

    [HarmonyPatch(typeof(OrbitalLaser), "OnBUpdate")]
    static class OrbitalLaserOnBUpdatePatch
    {
        private static readonly FieldInfo _stateField = AccessTools.Field(
            typeof(OrbitalLaser),
            "state"
        );

        static void Postfix(OrbitalLaser __instance)
        {
            if (!NetworkServer.active)
                return;

            var tracker = __instance.GetComponent<OrbitalLaserAircraftTracker>();
            if (tracker == null || tracker.AircraftTransform == null || tracker.Triggered)
                return;

            var state = (OrbitalLaserState)_stateField.GetValue(__instance);

            // (a) Track the aircraft during anticipation.
            if (
                state == OrbitalLaserState.AnticipationFollow
                || state == OrbitalLaserState.AnticipationStationary
            )
            {
                // Assigning NetworktargetPosition updates the SyncVar so all
                // clients' UpdatePosition calls use the current aircraft XZ.
                __instance.NetworktargetPosition = tracker.AircraftTransform.position;
                return;
            }

            // (b) Explosion phase — check XZ proximity to the aircraft.
            if (state == OrbitalLaserState.Exploding)
            {
                float xzDist = OrbitalLaserAircraftHelpers.XZDist(
                    __instance.transform.position,
                    tracker.AircraftTransform.position
                );
                float hitRadius = GameManager.ItemSettings.OrbitalLaserExplosionMaxRange;

                if (xzDist <= hitRadius)
                {
                    tracker.Triggered = true;
                    var callback = tracker.OnHit;
                    tracker.OnHit = null;
                    callback?.Invoke();
                    IssaPluginPlugin.Log.LogInfo("[OrbitalLaser] Aircraft hit confirmed.");
                }
            }
        }
    }
}
