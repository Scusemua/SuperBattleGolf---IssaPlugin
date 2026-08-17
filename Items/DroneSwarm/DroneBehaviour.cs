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
    /// Phase 1 — Swarming: each drone flies with Perlin-noise-driven steering.
    ///   Every frame a desired direction is sampled from two independent noise fields
    ///   (one for XZ, one for Y), smoothly rotated toward via Vector3.RotateTowards,
    ///   and then applied as a constant velocity — so the drone is always moving.
    ///   Each drone has a unique noise offset so all drones move independently.
    ///   Soft boundary forces prevent drones from leaving the wander area.
    ///   After the per-drone random swarm timer expires, transitions to Diving.
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

        /// World-space centre of the swarming area.
        public Vector3 WanderCenter;

        /// Radius (metres) of the soft boundary that keeps drones contained.
        public float WanderRadius = 40f;

        /// Constant flight speed while swarming (metres/second).
        public float WanderSpeed = 25f;

        /// Maximum steering rate while swarming (degrees/second).
        /// Higher values produce tighter, more erratic weaving.
        public float WanderTurnRate = 90f;

        /// Altitude band half-extent above/below WanderCenter.y the drones roam (metres).
        public float AltitudeVariance = 10f;

        /// How long (seconds) this drone swarms before picking a target and diving.
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

        private enum Phase
        {
            Swarming,
            Diving,
        }

        private Phase _phase = Phase.Swarming;
        private Rigidbody _rb;

        // ── Swarming ─────────────────────────────────────────────────────
        private float _noiseOffset; // unique per drone; shifts into a different part of the field
        private Vector3 _heading; // unit vector, rotated toward noise direction each frame
        private float _swarmTimer;

        // How fast the angular-velocity noise oscillates.  Higher = more frequent
        // direction reversals.  At 2.5 each drone changes turn-direction roughly 5
        // times per second, producing a tight bee-like waggle.
        private const float NoiseTimeScale = 2.5f;

        // How strongly drones are pushed back when they leave WanderRadius (XZ) or
        // the altitude band.  Expressed as a weight on the restoring direction.
        private const float BoundaryRestoreWeight = 3f;
        private const float AltitudeRestoreWeight = 2f;
        private const float DroneCollisionRange = 2f;

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

            // Each drone gets a large random offset so they all sample different
            // regions of the Perlin field and never move in sync.
            _noiseOffset = Random.Range(0f, 1000f);
            _swarmTimer = CircleTime;

            // Bootstrap an arbitrary forward heading; noise will steer it immediately.
            _heading = Random.onUnitSphere;
            _heading.y = 0f;
            if (_heading.sqrMagnitude < 0.001f)
                _heading = Vector3.forward;
            _heading.Normalize();
        }

        private void FixedUpdate()
        {
            // This component is server-only — guard defensively.
            if (!NetworkServer.active || _exploded)
                return;

            switch (_phase)
            {
                case Phase.Swarming:
                    UpdateSwarming();
                    break;
                case Phase.Diving:
                    UpdateDiving();
                    break;
            }
        }

        // ----------------------------------------------------------------
        //  Phase: Swarming
        // ----------------------------------------------------------------

        private void UpdateSwarming()
        {
            _swarmTimer -= Time.fixedDeltaTime;

            float t = Time.time * NoiseTimeScale + _noiseOffset;

            // Yaw-only noise: pitch is intentionally omitted.  Noise-driven pitch
            // gradually tilts _heading toward vertical, where yaw rotation around
            // world-up becomes a no-op — the drone gets stuck flying straight up or
            // down and appears motionless.  Altitude is handled separately below.
            float yawRate = (Mathf.PerlinNoise(t, _noiseOffset + 31.4f) * 2f - 1f) * WanderTurnRate;
            _heading = Quaternion.AngleAxis(yawRate * Time.fixedDeltaTime, Vector3.up) * _heading;

            // Keep heading strictly horizontal; clamp any accumulated float drift.
            _heading.y = 0f;
            if (_heading.sqrMagnitude < 0.001f)
                _heading = Vector3.forward;
            else
                _heading.Normalize();

            // Horizontal boundary correction: steer back toward WanderCenter XZ.
            Vector3 boundary = BoundaryForce();
            if (boundary.sqrMagnitude > 0.001f)
            {
                // Project onto XZ so this correction never tilts _heading vertically.
                Vector3 horizBoundary = new Vector3(boundary.x, 0f, boundary.z).normalized;
                _heading = Vector3.RotateTowards(
                    _heading,
                    horizBoundary,
                    boundary.magnitude * WanderTurnRate * Mathf.Deg2Rad * Time.fixedDeltaTime,
                    0f
                );
            }

            // Vertical altitude correction: added as a separate velocity component
            // so it doesn't tilt _heading.  Scaled to 40 % of WanderSpeed — gentle
            // enough not to dominate horizontal movement.
            Vector3 altVelocity = AltitudeForce() * (WanderSpeed * 0.4f);
            Vector3 velocity = _heading * WanderSpeed + altVelocity;

            _rb.MovePosition(transform.position + velocity * Time.fixedDeltaTime);
            RotateTowardStep(velocity);

            if (_swarmTimer > 0f)
                return;

            // Timer expired — pick a target and dive.
            Transform target = SelectRandomTarget();
            if (target == null)
            {
                // No valid targets yet; retry after a short interval.
                _swarmTimer = NoTargetRetryInterval;
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

        /// Returns a push-back vector when the drone drifts outside WanderRadius (XZ only).
        /// Grows linearly with excess distance so drones curve back rather than snap.
        private Vector3 BoundaryForce()
        {
            float dx = transform.position.x - WanderCenter.x;
            float dz = transform.position.z - WanderCenter.z;
            float flatDistSq = dx * dx + dz * dz;

            // Squared comparison avoids Sqrt in the common case (drone inside boundary).
            if (flatDistSq <= WanderRadius * WanderRadius)
                return Vector3.zero;

            float flatDist = Mathf.Sqrt(flatDistSq);
            float excess = (flatDist - WanderRadius) / WanderRadius;
            return new Vector3(-dx, 0f, -dz).normalized * (excess * BoundaryRestoreWeight);
        }

        /// Returns a vertical push when the drone leaves the altitude band.
        private Vector3 AltitudeForce()
        {
            float altMin = WanderCenter.y - AltitudeVariance;
            float altMax = WanderCenter.y + AltitudeVariance;
            float y = transform.position.y;

            if (y < altMin)
                return Vector3.up * ((altMin - y) / AltitudeVariance * AltitudeRestoreWeight);
            if (y > altMax)
                return Vector3.down * ((y - altMax) / AltitudeVariance * AltitudeRestoreWeight);

            return Vector3.zero;
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

            // CheckSphere exits on the first hit and needs no external buffer,
            // making it cheaper than OverlapSphereNonAlloc for a pure yes/no test.
            if (
                Physics.CheckSphere(
                    transform.position,
                    DroneCollisionRange,
                    GameManager.LayerSettings.RocketHittablesMask,
                    QueryTriggerInteraction.Ignore
                )
            )
            {
                IssaPluginPlugin.Log.LogInfo("[Drone] Collision while diving.");
                Detonate();
                return;
            }

            // Update the homing aim point while still actively tracking.
            // Note: only null-check the transform — relying on activeInHierarchy
            // can suppress updates when the player GameObject is temporarily
            // considered inactive (e.g. during certain animation states).
            if (_homingActive && _diveTargetTransform != null)
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
            Vector3 step = toTarget.normalized * _currentDiveSpeed * Time.fixedDeltaTime;
            if (step.magnitude > dist)
                step = toTarget;

            RotateTowardStep(step);
            _rb.MovePosition(transform.position + step);
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

            // When the drone approaches from above, its position is higher than the
            // player's.  The game computes knockback as (playerPos - explosionPos),
            // so an explosion above the player produces a downward force that drives
            // them through the terrain.  Cap the explosion Y to the target's feet so
            // the vector always points upward/outward rather than downward.
            if (_diveTargetTransform != null)
                pos.y = Mathf.Min(pos.y, _diveTargetTransform.position.y + 0.5f);

            IssaPluginPlugin.Log.LogDebug($"[Drone] Detonating at {pos:F1}.");

            // Tell all clients to spawn the custom explosion VFX.
            NetworkServer.SendToAll(new DroneExplodedMessage { Position = pos });

            // Spawn a temporary game Rocket and immediately ServerExplode it so the
            // game's own damage radius and ExplosionScaler handle knockback correctly.
            var tempRocket = Instantiate(
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
        //  Target selection — called once when the swarm timer expires
        // ----------------------------------------------------------------

        // Reuse a static list to avoid heap allocation on each target-selection call.
        private static readonly List<Transform> _candidateScratch = [];

        private Transform SelectRandomTarget()
        {
            // Use the same GameManager player lists that BearBehaviour uses — these
            // hold the authoritative, live-position transforms for every player and
            // require no scene search.
            _candidateScratch.Clear();

            var local = GameManager.LocalPlayerInfo;
            if (local != null && IsValidTarget(local))
                _candidateScratch.Add(local.transform);

            var remotes = GameManager.RemotePlayers;
            if (remotes != null)
                foreach (var p in remotes)
                    if (IsValidTarget(p))
                        _candidateScratch.Add(p.transform);

            return _candidateScratch.Count == 0
                ? null
                : _candidateScratch[Random.Range(0, _candidateScratch.Count)];
        }

        private bool IsValidTarget(PlayerInfo player)
        {
            if (player == null || !player.gameObject.activeInHierarchy)
                return false;

            if (
                !FriendlyFire
                && ThrowerInfo != null
                && player.PlayerId.Guid == ThrowerInfo.PlayerId.Guid
            )
                return false;

            if (
                !AttackFinishedPlayers
                && player.AsGolfer?.MatchResolution == PlayerMatchResolution.Scored
            )
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

            // Use _rb.MoveRotation so both position and rotation go through the
            // physics engine in the same tick.  Writing transform.rotation directly
            // while a MovePosition is pending on a kinematic Rigidbody cancels the
            // pending position update in some Unity versions.
            Quaternion target = DirectionToRotation(direction.normalized);
            _rb.MoveRotation(
                Quaternion.RotateTowards(transform.rotation, target, 720f * Time.fixedDeltaTime)
            );
        }

        private static Quaternion DirectionToRotation(Vector3 direction)
        {
            if (direction.sqrMagnitude < 0.001f)
                return Quaternion.identity;

            Vector3 up =
                Mathf.Abs(Vector3.Dot(direction, Vector3.up)) > 0.99f
                    ? Vector3.forward
                    : Vector3.up;

            return Quaternion.LookRotation(direction, up);
        }
    }
}
