using System.Collections.Generic;
using System.Reflection;
using Mirror;
using UnityEngine;

namespace IssaPlugin.Items
{
    /// <summary>
    /// Server-only per-drone AI. Added programmatically by DroneSwarmNetworkBridge
    /// after NetworkServer.Spawn, so it only runs on the server. Clients receive
    /// position and rotation updates through the prefab's NetworkTransform.
    ///
    /// Phase 1 — Wandering: each drone picks a random 3D waypoint within WanderRadius
    ///   of WanderCenter, smoothly steers toward it at WanderSpeed, then picks another
    ///   when it arrives. AltitudeVariance lets waypoints scatter vertically so drones
    ///   weave at different heights. After the per-drone random circle timer expires,
    ///   the drone transitions to Diving.
    ///
    /// Phase 2 — Diving: the drone accelerates toward the locked target's live world
    ///   position each frame. Once within HomingStopDistance of the target (configurable),
    ///   it stops updating the aim point and flies straight — giving the target a chance
    ///   to dodge. Setting HomingStopDistance to 0 makes the drone always home.
    ///
    /// Detonation uses the temporary-Rocket + ServerExplode pattern (same as Nuke and
    /// Harrier) so the game's own damage and ExplosionScaler systems apply.
    /// </summary>
    public class DroneBehaviour : MonoBehaviour
    {
        // ----------------------------------------------------------------
        //  Configuration — set by DroneSwarmNetworkBridge before Start fires
        // ----------------------------------------------------------------

        /// World-space centre of the wandering area.
        public Vector3 WanderCenter;

        /// Maximum distance from WanderCenter a waypoint may be placed (metres).
        public float WanderRadius = 40f;

        /// Cruise speed while wandering (metres/second).
        public float WanderSpeed = 25f;

        /// Maximum steering rate while wandering (degrees/second).
        /// Lower = wider, lazier arcs. Higher = tighter turns.
        public float WanderTurnRate = 60f;

        /// Half-range for random altitude variation added to each new waypoint (metres).
        /// Waypoints are placed between WanderCenter.y ± AltitudeVariance.
        public float AltitudeVariance = 10f;

        /// How long (seconds) this drone wanders before picking a target and diving.
        /// Randomised per-drone by the bridge.
        public float CircleTime = 5f;

        /// Initial dive speed in metres/second.
        public float DiveSpeed = 30f;

        /// Acceleration applied every second during the dive (metres/s²).
        public float DiveAcceleration = 20f;

        /// Distance from the target (metres) at which the drone stops updating its
        /// aim point and flies straight. 0 = always home.
        public float HomingStopDistance = 15f;

        /// Detonation trigger radius in metres.
        public float ArrivalRadius = 3f;

        /// Multiplier passed to ExplosionScaler for the impact blast.
        public float ExplosionScale = 1.5f;

        /// PlayerInfo of the player who summoned this drone (for kill attribution).
        public PlayerInfo ThrowerInfo;

        /// Pre-built ItemUseId for the detonation rocket (for kill attribution).
        public ItemUseId ItemUseId;

        /// Whether this drone may target the summoner.
        public bool FriendlyFire;

        /// Whether this drone may target players who have already holed out.
        public bool AttackFinishedPlayers;

        // ----------------------------------------------------------------
        //  Internal state
        // ----------------------------------------------------------------

        private enum Phase { Wandering, Diving }

        private Phase _phase = Phase.Wandering;
        private Rigidbody _rb;

        // ── Wandering ────────────────────────────────────────────────────
        private Vector3 _wanderTarget;
        private Vector3 _currentHeading;   // unit vector, updated each frame
        private float _circleTimer;
        private const float WaypointArrivalRadius = 5f;

        // ── Diving ───────────────────────────────────────────────────────
        private Transform _diveTargetTransform;
        private Vector3 _homingTarget;
        private bool _homingActive;
        private float _currentDiveSpeed;
        private float _divingTimeoutTimer;

        private bool _exploded;

        // Safety: force-detonate if the dive lasts longer than this.
        private const float DivingTimeout = 30f;

        // How long to wait before retrying target selection when no valid targets exist.
        private const float NoTargetRetryInterval = 1f;

        private static readonly MethodInfo ServerExplodeMethod = typeof(Rocket).GetMethod(
            "ServerExplode",
            BindingFlags.NonPublic | BindingFlags.Instance
        );

        // ----------------------------------------------------------------
        //  Unity lifecycle
        // ----------------------------------------------------------------

        private void Start()
        {
            _rb = GetComponent<Rigidbody>();
            if (_rb == null)
                _rb = gameObject.AddComponent<Rigidbody>();
            _rb.isKinematic = true;
            _rb.useGravity = false;

            _circleTimer = CircleTime;
            _wanderTarget = PickRandomWaypoint();

            // Steer toward the first waypoint immediately.
            Vector3 toFirst = _wanderTarget - transform.position;
            _currentHeading = toFirst.sqrMagnitude > 0.001f
                ? toFirst.normalized
                : Vector3.forward;
        }

        private void FixedUpdate()
        {
            // This component is server-only — guard defensively.
            if (!NetworkServer.active || _exploded)
                return;

            switch (_phase)
            {
                case Phase.Wandering: UpdateWandering(); break;
                case Phase.Diving:    UpdateDiving();    break;
            }
        }

        // ----------------------------------------------------------------
        //  Phase: Wandering
        // ----------------------------------------------------------------

        private void UpdateWandering()
        {
            _circleTimer -= Time.fixedDeltaTime;

            // Pick a new waypoint once the drone arrives near the current one.
            if (Vector3.Distance(transform.position, _wanderTarget) < WaypointArrivalRadius)
                _wanderTarget = PickRandomWaypoint();

            // Smoothly rotate heading toward the waypoint direction.
            Vector3 desired = (_wanderTarget - transform.position).normalized;
            _currentHeading = Vector3.RotateTowards(
                _currentHeading,
                desired,
                WanderTurnRate * Mathf.Deg2Rad * Time.fixedDeltaTime,
                0f
            );

            _rb.MovePosition(transform.position + _currentHeading * (WanderSpeed * Time.fixedDeltaTime));
            RotateTowardStep(_currentHeading);

            if (_circleTimer > 0f)
                return;

            // Timer expired — try to pick a target.
            Transform target = SelectRandomTarget();
            if (target == null)
            {
                // No valid targets yet; retry after a short interval.
                _circleTimer = NoTargetRetryInterval;
                return;
            }

            _diveTargetTransform = target;
            _homingTarget = target.position;
            _homingActive = true;
            _currentDiveSpeed = DiveSpeed;
            _divingTimeoutTimer = DivingTimeout;
            _phase = Phase.Diving;

            IssaPluginPlugin.Log.LogInfo(
                $"[Drone] Transitioning to dive — target {target.name} at {target.position:F1}."
            );
        }

        private Vector3 PickRandomWaypoint()
        {
            float angle  = Random.Range(0f, 360f) * Mathf.Deg2Rad;
            // Bias toward the outer half of the area so drones don't clump in the centre.
            float radius = Random.Range(WanderRadius * 0.25f, WanderRadius);
            float altOffset = Random.Range(-AltitudeVariance, AltitudeVariance);

            return new Vector3(
                WanderCenter.x + Mathf.Sin(angle) * radius,
                WanderCenter.y + altOffset,
                WanderCenter.z + Mathf.Cos(angle) * radius
            );
        }

        // ----------------------------------------------------------------
        //  Phase: Diving
        // ----------------------------------------------------------------

        private void UpdateDiving()
        {
            _divingTimeoutTimer -= Time.fixedDeltaTime;
            if (_divingTimeoutTimer <= 0f)
            {
                IssaPluginPlugin.Log.LogInfo("[Drone] Dive timed out — force detonating.");
                Detonate();
                return;
            }

            // Update the homing aim point while still actively tracking.
            if (_homingActive
                && _diveTargetTransform != null
                && _diveTargetTransform.gameObject.activeInHierarchy)
            {
                float distToTarget = Vector3.Distance(
                    transform.position,
                    _diveTargetTransform.position
                );

                bool stopHoming = HomingStopDistance > 0f && distToTarget <= HomingStopDistance;
                if (!stopHoming)
                    _homingTarget = _diveTargetTransform.position;
                else
                    _homingActive = false;
            }

            // Arrival check before moving so we don't overshoot silently.
            float dist = Vector3.Distance(transform.position, _homingTarget);
            if (dist < ArrivalRadius)
            {
                Detonate();
                return;
            }

            // Accelerate.
            _currentDiveSpeed += DiveAcceleration * Time.fixedDeltaTime;

            // Compute movement step — don't overshoot the target position.
            Vector3 toTarget = _homingTarget - transform.position;
            Vector3 step = toTarget.normalized * (_currentDiveSpeed * Time.fixedDeltaTime);
            if (step.magnitude > dist)
                step = toTarget;

            _rb.MovePosition(transform.position + step);
            RotateTowardStep(step);
        }

        // ----------------------------------------------------------------
        //  Detonation
        // ----------------------------------------------------------------

        public void Detonate()
        {
            if (_exploded)
                return;
            _exploded = true;

            Vector3 pos = transform.position;
            IssaPluginPlugin.Log.LogInfo($"[Drone] Detonating at {pos:F1}.");

            // Tell all clients to spawn the custom explosion VFX.
            NetworkServer.SendToAll(new DroneExplodedMessage { Position = pos });

            // Spawn a temporary game Rocket and immediately ServerExplode it so the
            // game's own damage radius and ExplosionScaler handle knockback correctly.
            var tempRocket = Object.Instantiate(
                GameManager.ItemSettings.RocketPrefab,
                pos,
                Quaternion.identity
            );

            if (tempRocket != null)
            {
                tempRocket.gameObject.AddComponent<CustomSpawnedRocket>();
                tempRocket.ServerInitialize(ThrowerInfo, null, ItemUseId);
                NetworkServer.Spawn(tempRocket.gameObject, (NetworkConnectionToClient)null);
                ExplosionScaler.Register(tempRocket, ExplosionScale);
                ServerExplodeMethod?.Invoke(tempRocket, new object[] { pos });
            }

            // Destroy this object — wakes the WatchDrone coroutine in the bridge.
            NetworkServer.Destroy(gameObject);
        }

        // ----------------------------------------------------------------
        //  Target selection — called once when the wander timer expires
        // ----------------------------------------------------------------

        private Transform SelectRandomTarget()
        {
            var candidates = new List<Transform>();
            foreach (var inv in Object.FindObjectsByType<PlayerInventory>(FindObjectsSortMode.None))
            {
                if (IsValidTarget(inv))
                    candidates.Add(inv.transform);
            }

            if (candidates.Count == 0)
                return null;

            return candidates[Random.Range(0, candidates.Count)];
        }

        private bool IsValidTarget(PlayerInventory inv)
        {
            if (inv == null || inv.gameObject == null || !inv.gameObject.activeInHierarchy)
                return false;

            if (!FriendlyFire
                && ThrowerInfo != null
                && inv.PlayerInfo?.PlayerId.Guid == ThrowerInfo.PlayerId.Guid)
                return false;

            if (!AttackFinishedPlayers
                && inv.PlayerInfo?.AsGolfer != null
                && inv.PlayerInfo.AsGolfer.MatchResolution == PlayerMatchResolution.Scored)
                return false;

            return true;
        }

        // ----------------------------------------------------------------
        //  Rotation helpers (same pattern as JavelinRocketBehaviour)
        // ----------------------------------------------------------------

        private void RotateTowardStep(Vector3 direction)
        {
            if (direction.sqrMagnitude < 0.0001f)
                return;

            Quaternion target = DirectionToRotation(direction.normalized);
            transform.rotation = Quaternion.RotateTowards(
                transform.rotation,
                target,
                720f * Time.fixedDeltaTime
            );
        }

        private static Quaternion DirectionToRotation(Vector3 direction)
        {
            if (direction.sqrMagnitude < 0.001f)
                return Quaternion.identity;

            Vector3 up = Mathf.Abs(Vector3.Dot(direction, Vector3.up)) > 0.99f
                ? Vector3.forward
                : Vector3.up;

            return Quaternion.LookRotation(direction, up);
        }
    }
}
