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
    /// Phase 1 — Circling: the drone orbits OrbitCenter at a fixed radius and
    ///   altitude using a parametric circle, waiting for its per-drone random
    ///   circle timer to expire.
    ///
    /// Phase 2 — Diving: the drone accelerates toward the locked target's live
    ///   world position each frame. Once the drone comes within HomingStopDistance
    ///   of the target (configurable), it stops updating the aim point and flies
    ///   straight to the last-known position — giving the target a chance to dodge.
    ///   Setting HomingStopDistance to 0 disables this behaviour and makes the
    ///   drone always home until impact.
    ///
    /// Detonation uses the temporary-Rocket + ServerExplode pattern (same as Nuke
    /// and Harrier) so the game's own damage and ExplosionScaler systems apply.
    /// </summary>
    public class DroneBehaviour : MonoBehaviour
    {
        // ----------------------------------------------------------------
        //  Configuration — set by DroneSwarmNetworkBridge before Start fires
        // ----------------------------------------------------------------

        /// World-space centre of the orbit circle.
        public Vector3 OrbitCenter;

        /// Radius of the orbit circle in metres.
        public float OrbitRadius = 40f;

        /// How many degrees per second the drone advances around the orbit.
        public float OrbitSpeed = 45f;

        /// Starting angle of this specific drone on the orbit, in degrees.
        /// Assigned by the bridge so drones are spread evenly.
        public float InitialOrbitAngle = 0f;

        /// How long (seconds) this drone circles before picking a target and diving.
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

        private enum Phase { Circling, Diving }

        private Phase _phase = Phase.Circling;
        private Rigidbody _rb;

        private float _orbitAngle;    // radians, incremented each FixedUpdate
        private float _circleTimer;

        private Transform _diveTargetTransform; // null once homing has stopped
        private Vector3 _homingTarget;           // world point the drone currently flies toward
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

            _orbitAngle = InitialOrbitAngle * Mathf.Deg2Rad;
            _circleTimer = CircleTime;
        }

        private void FixedUpdate()
        {
            // This component is server-only — guard defensively.
            if (!NetworkServer.active || _exploded)
                return;

            switch (_phase)
            {
                case Phase.Circling: UpdateCircling(); break;
                case Phase.Diving:   UpdateDiving();   break;
            }
        }

        // ----------------------------------------------------------------
        //  Phase: Circling
        // ----------------------------------------------------------------

        private void UpdateCircling()
        {
            _circleTimer -= Time.fixedDeltaTime;

            // Advance orbit angle: positive increment produces counter-clockwise
            // motion when viewed from above.
            _orbitAngle += OrbitSpeed * Mathf.Deg2Rad * Time.fixedDeltaTime;

            // Parametric circle — X = sin(θ)·r, Z = cos(θ)·r.
            float x = Mathf.Sin(_orbitAngle) * OrbitRadius;
            float z = Mathf.Cos(_orbitAngle) * OrbitRadius;
            Vector3 newPos = new Vector3(OrbitCenter.x + x, OrbitCenter.y, OrbitCenter.z + z);
            _rb.MovePosition(newPos);

            // Face the tangent direction of travel: d/dθ of (sin·r, 0, cos·r) = (cos, 0, −sin).
            Vector3 tangent = new Vector3(Mathf.Cos(_orbitAngle), 0f, -Mathf.Sin(_orbitAngle));
            if (tangent.sqrMagnitude > 0.001f)
                _rb.MoveRotation(Quaternion.LookRotation(tangent));

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

            // Update the homing aim point while we are still actively tracking.
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
                    _homingActive = false; // from here the drone flies straight
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

            // Destroy the drone object itself — this wakes the WatchDrone coroutine
            // in DroneSwarmNetworkBridge which decrements the active count.
            NetworkServer.Destroy(gameObject);
        }

        // ----------------------------------------------------------------
        //  Target selection — called once when the circle timer expires
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
        //  Rotation helpers (identical to JavelinRocketBehaviour)
        // ----------------------------------------------------------------

        private void RotateTowardStep(Vector3 step)
        {
            if (step.sqrMagnitude < 0.0001f)
                return;

            Quaternion target = DirectionToRotation(step.normalized);
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
