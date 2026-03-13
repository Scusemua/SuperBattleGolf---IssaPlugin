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
    //  After the normal nearest-player search, check whether the AC130
    //  gunship or stealth bomber is active and is closer to the hole than
    //  the nearest player. If so, override the return value to
    //  (null Hittable, aircraftPosition) so the laser fires in stationary
    //  mode with the indicator tracking the aircraft's ground projection.
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
            Vector3 holePos = GolfHoleManager.MainHole.transform.position;

            // Baseline: how far is the current best player target from the hole?
            float bestDist =
                __result != null
                    ? OrbitalLaserAircraftHelpers.XZDist(fallbackPosition, holePos)
                    : float.MaxValue;

            Transform bestAircraft = null;

            // AC130 gunship — marker added on all clients via RpcAddGunshipLockOnComponents.
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

            // Bomber proxy — BomberMarker added on all clients via RpcAddBomberLockOnComponents.
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
        // Generous proximity threshold to absorb network lag between the
        // client's aircraft position and the server's authoritative position.
        private const float MatchRadius = 100f;

        static void Postfix(OrbitalLaser __instance, Hittable target, Vector3 fallbackWorldPosition)
        {
            if (!NetworkServer.active || target != null)
                return;

            Transform bestAircraft = null;
            System.Action onHit = null;
            float bestDist = MatchRadius;

            // AC130 gunship
            var gunship = AC130NetworkBridge.ActiveGunship;
            if (gunship != null)
            {
                float d = OrbitalLaserAircraftHelpers.XZDist(gunship.transform.position, fallbackWorldPosition);
                if (d < bestDist)
                {
                    bestDist = d;
                    bestAircraft = gunship.transform;
                    var hitReceiver = gunship.GetComponent<AC130HitReceiver>();
                    onHit = () => hitReceiver?.OnAC130Hit();
                }
            }

            // Bomber proxy
            var proxy = Object.FindFirstObjectByType<BomberProxyBehaviour>();
            if (proxy != null)
            {
                float d = OrbitalLaserAircraftHelpers.XZDist(proxy.transform.position, fallbackWorldPosition);
                if (d < bestDist)
                {
                    bestDist = d;
                    bestAircraft = proxy.transform;
                    onHit = () => proxy.TriggerShotDown();
                }
            }

            if (bestAircraft == null)
                return;

            var tracker = __instance.gameObject.AddComponent<OrbitalLaserAircraftTracker>();
            tracker.AircraftTransform = bestAircraft;
            tracker.OnHit = onHit;

            IssaPluginPlugin.Log.LogInfo(
                $"[OrbitalLaser] Aircraft tracker attached — {bestAircraft.name} "
                    + $"at {bestAircraft.position}."
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
