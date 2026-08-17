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

        public bool WasShotFromDonut;
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

        // ────────────────────────────────────────────────────────────────
        //  Aircraft marker cache
        //
        //  These lookups sit on OrbitalLaserManager.GetTarget, which the base game
        //  calls from PlayerInventory.OnBUpdate — every frame, for as long as the
        //  local player holds a laser-targeting item. Resolving them with
        //  FindFirstObjectByType meant three full scene walks per frame, which is
        //  why holding the Donut or Orbital Laser cost frames regardless of what
        //  was drawn on screen.
        //
        //  A live cached marker is returned directly. When none is cached, the scan
        //  is rate-limited to RescanInterval so the common "no aircraft exists" case
        //  costs a timestamp comparison per frame instead of three scene walks.
        // ────────────────────────────────────────────────────────────────

        private static AC130GunshipMarker _gunship;
        private static BomberMarker _bomber;
        private static DonutMarker _donut;

        // Rescanning is rate-limited rather than done per frame. Markers appear only
        // when someone activates an aircraft item, so the overwhelmingly common case is
        // "none exist" — and that case must not pay for a scan every frame, which is
        // exactly what the previous FindFirstObjectByType calls did.
        private const float RescanInterval = 0.5f;

        // One timer per marker type, indexed by TimerSlot<T>.Index. Sized generously;
        // only three slots are used today.
        private static readonly float[] _nextRescanTime = new float[8];
        private static int _nextSlot = -1;

        /// Forces the next lookup of every marker type to rescan immediately. Call when
        /// an aircraft is known to have spawned so it is picked up without waiting out
        /// the rescan interval.
        public static void Invalidate()
        {
            for (int i = 0; i < _nextRescanTime.Length; i++)
                _nextRescanTime[i] = 0f;
        }

        public static AC130GunshipMarker Gunship => Refresh(ref _gunship);

        public static BomberMarker Bomber => Refresh(ref _bomber);

        public static DonutMarker Donut => Refresh(ref _donut);

        /// Returns the cached marker, rescanning for it only when the cache is empty
        /// and the rescan interval has elapsed.
        ///
        /// Each marker type is tracked independently and on its own timer. The three
        /// aircraft use separate GlobalSessionLock types, so a donut, an AC130 and a
        /// stealth bomber can all be airborne at once, and one being active must never
        /// suppress the search for the others — an earlier version early-returned when
        /// any one marker was live, which meant an aircraft launched during another's
        /// session was never picked up.
        private static T Refresh<T>(ref T cached)
            where T : Component
        {
            // Unity's fake-null makes a destroyed marker compare equal to null, so this
            // also catches markers whose GameObject went away.
            if (cached != null)
                return cached;

            // Rate-limit the miss path: with no aircraft airborne — the common case —
            // this must not scan the scene every frame, which is the cost being removed.
            int slot = TimerSlot<T>.Index;
            if (Time.unscaledTime < _nextRescanTime[slot])
                return null;

            _nextRescanTime[slot] = Time.unscaledTime + RescanInterval;

            cached = Object.FindFirstObjectByType<T>();
            return cached;
        }

        /// Assigns each marker type a stable index into _nextRescanTime so the three
        /// types back off independently rather than sharing one timer.
        private static class TimerSlot<T>
        {
            internal static readonly int Index = System.Threading.Interlocked.Increment(
                ref _nextSlot
            );
        }
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

            var gunshipMarker = OrbitalLaserAircraftHelpers.Gunship;
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

            var bomberMarker = OrbitalLaserAircraftHelpers.Bomber;
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

            var donutMarker = OrbitalLaserAircraftHelpers.Donut;
            if (donutMarker != null)
            {
                float d = OrbitalLaserAircraftHelpers.XZDist(
                    donutMarker.transform.position,
                    holePos
                );
                if (d < bestDist)
                {
                    bestDist = d;
                    bestAircraft = donutMarker.transform;
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
    //  we feed it directly to LocalPlayerSetLockOnTarget and skip the base method
    //  so the standard lock-on reticle tracks the aircraft just like it would a player.
    // ====================================================================

    [HarmonyPatch]
    static class OrbitalLaserLockOnIndicatorPatch
    {
        static MethodInfo TargetMethod() =>
            typeof(PlayerInventory)
                .GetMethods(BindingFlags.NonPublic | BindingFlags.Instance)
                .FirstOrDefault(m => m.Name.Contains("UpdateOrbitalLaserLockOnTarget"));

        private static readonly MethodInfo _localPlayerSetLockOnTarget = AccessTools.Method(
            typeof(PlayerInventory),
            "LocalPlayerSetLockOnTarget"
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

            var gunshipMarker = OrbitalLaserAircraftHelpers.Gunship;
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

            var bomberMarker = OrbitalLaserAircraftHelpers.Bomber;
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

            if (bestLockOn == null)
                return true; // no aircraft — let base method run normally

            // Point the lock-on reticle at the aircraft and skip the base method.
            _localPlayerSetLockOnTarget.Invoke(__instance, new object[] { bestLockOn });
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
        static Transform FindClosestAircraft(Vector3 fallbackWorldPosition, out System.Action onHit)
        {
            Transform bestAircraft = null;
            onHit = null;
            float bestDist = float.MaxValue;

            // AC130 gunship — AC130GunshipMarker is added server-side in AC130NetworkBridge.SpawnGunship.
            var gunshipMarker = OrbitalLaserAircraftHelpers.Gunship;
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

            // // Donut
            // var donutHitReceiver = Object.FindFirstObjectByType<DonutHitReceiver>();
            // if (donutHitReceiver != null)
            // {
            //     float d = OrbitalLaserAircraftHelpers.XZDist(
            //         donutHitReceiver.transform.position,
            //         fallbackWorldPosition
            //     );
            //     if (d < bestDist)
            //     {
            //         bestDist = d;
            //         bestAircraft = donutHitReceiver.transform;
            //         onHit = () => donutHitReceiver.OnHit();
            //     }
            // }

            return bestAircraft;
        }

        static bool Prefix(
            OrbitalLaser __instance,
            Hittable target,
            Vector3 fallbackWorldPosition,
            PlayerInventory owner,
            ItemUseId itemUseId
        )
        {
            if (!NetworkServer.active || target != null)
                return true;

            if (fallbackWorldPosition == DonutNetworkBridge.DonutLaserTargetVector)
            {
                var ownerField = typeof(OrbitalLaser).GetField(
                    "owner",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
                );
                ownerField.SetValue(__instance, owner);

                var itemUseIdField = typeof(OrbitalLaser).GetField(
                    "itemUseId",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
                );
                itemUseIdField.SetValue(__instance, itemUseId);

                var activationTimestampField = typeof(OrbitalLaser).GetField(
                    "activationTimestamp",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
                );
                activationTimestampField.SetValue(__instance, Time.timeAsDouble);

                __instance.NetworktargetPosition = fallbackWorldPosition;
                return false;
            }

            return true;
        }

        static void Postfix(
            OrbitalLaser __instance,
            Hittable target,
            Vector3 fallbackWorldPosition,
            PlayerInventory owner,
            ItemUseId itemUseId
        )
        {
            if (!NetworkServer.active || target != null)
                return;

            System.Action onHit = null;
            Transform bestAircraft = null;
            bool donutShootingLaser = false;
            if (fallbackWorldPosition == DonutNetworkBridge.DonutLaserTargetVector)
            {
                donutShootingLaser = true;
                var activeDonutFlyBehaviour =
                    DonutNetworkBridge.ActiveDonut.GetComponent<DonutFlyBehaviour>();
                if (activeDonutFlyBehaviour != null)
                {
                    bestAircraft = activeDonutFlyBehaviour.DonutLaserTarget.transform;
                }
            }
            else
            {
                // Pick the aircraft closest to fallbackWorldPosition (the position the
                // client reported when firing). We use FindFirstObjectByType here —
                // not AC130NetworkBridge.ActiveGunship — because ActiveGunship becomes
                // null once the session ends (fly-out), even though the gunship
                // GameObject still exists and the laser should still track it.
                bestAircraft = FindClosestAircraft(fallbackWorldPosition, out onHit);
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
            tracker.WasShotFromDonut = donutShootingLaser;

            // Snap NetworktargetPosition to the server-authoritative aircraft position
            // so the beam starts at the correct location even if there was slight
            // client/server position drift during the activation delay.
            __instance.NetworktargetPosition = bestAircraft.position;

            if (donutShootingLaser && bestAircraft != null)
            {
                __instance.Networkstate = OrbitalLaserState.Exploding;
            }

            IssaPluginPlugin.Log.LogInfo(
                $"[OrbitalLaser] Tracker attached to {bestAircraft.name} "
                    + $"at {bestAircraft.position}.)."
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
            // Use NetworktargetPosition (which we track to the aircraft during anticipation)
            // rather than transform.position (the OrbitalLaser component's own world pos,
            // which is unrelated to where the beam lands).
            if (state == OrbitalLaserState.Exploding)
            {
                float xzDist = OrbitalLaserAircraftHelpers.XZDist(
                    __instance.NetworktargetPosition,
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
