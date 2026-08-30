using System.Reflection;
using Mirror;
using UnityEngine;

namespace IssaPlugin.Items
{
    /// <summary>
    /// Server-only homing behaviour added to the hunter drone after NetworkServer.Spawn.
    ///
    /// Unlike DroneBehaviour (which has a circling phase), HunterDroneBehaviour skips
    /// straight to homing: the bridge assigns the player the thrower was aiming at
    /// (see <see cref="HunterDroneTargeting"/>) and the drone flies directly at them.
    /// Once within ArrivalRadius the drone detonates.
    ///
    /// The target is chosen once, at launch. If it later becomes invalid (the player holed
    /// out, disconnected) the drone flies on in its launch direction rather than re-acquiring
    /// someone the thrower never aimed at.
    ///
    /// The drone can also be destroyed mid-flight by any projectile (bullet or rocket);
    /// the <see cref="ShootDown"/> method handles that path.
    ///
    /// Detonation uses the temporary-Rocket + ServerExplode pattern (same as DroneBehaviour
    /// and Harrier) so the game's own damage and ExplosionScaler systems apply.
    /// </summary>
    public class HunterDroneBehaviour : CustomHittable
    {
        // ----------------------------------------------------------------
        //  Configuration — set by HunterDroneNetworkBridge before Start fires
        // ----------------------------------------------------------------

        /// Initial flight speed in metres/second.
        public float LaunchSpeed = 35f;

        /// Acceleration applied every second during flight (metres/s²).
        public float Acceleration = 25f;

        /// Distance from the target (metres) at which the drone stops updating
        /// its aim point and flies straight. 0 = always home.
        public float HomingStopDistance = 10f;

        /// Detonation trigger radius in metres.
        public float ArrivalRadius = 0.5f;

        /// Multiplier passed to ExplosionScaler for the impact blast.
        public float ExplosionScale = 1.2f;

        /// PlayerInfo of the player who threw this drone (for kill attribution).
        public PlayerInfo ThrowerInfo;

        /// Pre-built ItemUseId for the detonation rocket (for kill attribution).
        public ItemUseId ItemUseId;

        /// Whether this drone may target the thrower.
        public bool FriendlyFire;

        /// Whether this drone may target players who have already holed out.
        public bool AttackFinishedPlayers;

        /// Seconds after spawning during which collision detection is suppressed
        /// so the drone can clear the thrower's body before arming.
        public float ArmDelay = 0.4f;

        /// Maximum distance (metres) the drone may travel before self-detonating.
        public float MaxFlightDistance = 500f;

        /// Maximum flight speed in metres/second. Caps acceleration-driven growth.
        public float MaxSpeed = 300f;

        // Direction the drone should fly when no target exists, computed once at launch
        // from the player's aim point so the drone travels toward where they pointed.
        private Vector3 _launchDirection;
        private bool _hasLaunchDirection;

        // ----------------------------------------------------------------
        //  Internal state
        // ----------------------------------------------------------------

        private Rigidbody _rb;
        private Transform _targetTransform;
        private PlayerInfo _targetInfo;
        private Vector3 _homingTarget;
        private bool _homingActive;
        private float _currentSpeed;
        private bool _armed;
        private float _armTimer;
        private bool _exploded;
        private float _distanceTraveled;

        // How often the assigned target is re-checked for validity, in seconds.
        private const float TargetValidateInterval = 0.5f;
        private float _validateTimer;

        private static readonly MethodInfo ServerExplodeMethod = typeof(Rocket).GetMethod(
            "ServerExplode",
            BindingFlags.NonPublic | BindingFlags.Instance
        );

        // Per-instance buffer for the self-filtered collision check.
        private readonly Collider[] _collisionBuffer = new Collider[8];

        // ----------------------------------------------------------------
        //  Unity lifecycle
        // ----------------------------------------------------------------

        /// <summary>
        /// Assigns the player this drone hunts. Called by the bridge before the drone's
        /// first FixedUpdate, using the target the thrower was aiming at.
        /// </summary>
        public void SetTarget(PlayerInfo target)
        {
            if (target == null)
                return;

            _targetInfo = target;
            _targetTransform = target.transform;
            _homingTarget = _targetTransform.position;
            _homingActive = true;
        }

        public void SetFallbackAimPoint(Vector3 worldPoint)
        {
            // Store as a unit vector computed from spawn position to aim point so the
            // direction stays stable — it doesn't flip when the drone flies past the point.
            Vector3 dir = worldPoint - transform.position;
            if (dir.sqrMagnitude > 0.001f)
            {
                _launchDirection = dir.normalized;
                _hasLaunchDirection = true;
            }
        }

        private void Start()
        {
            _rb = GetComponent<Rigidbody>();
            if (_rb == null)
                _rb = gameObject.AddComponent<Rigidbody>();
            _rb.isKinematic = true;
            _rb.useGravity = false;

            _currentSpeed = LaunchSpeed;
            _armTimer = ArmDelay;

            // Invoke HandleHit on any hit — the drone is destroyed in one hit.
            OnHit += HandleHit;

            var particles = gameObject.GetComponentsInChildren<ParticleSystem>(
                includeInactive: true
            );
            foreach (var ps in particles)
            {
                ps.gameObject.SetActive(true);
                ps.Play();
            }
        }

        private void FixedUpdate()
        {
            if (!NetworkServer.active || _exploded)
                return;

            // Arm delay: suppress collision detection briefly so the drone clears
            // the thrower before it starts checking for detonation triggers.
            if (!_armed)
            {
                _armTimer -= Time.fixedDeltaTime;
                if (_armTimer <= 0f)
                    _armed = true;
            }

            // Re-validate the assigned target periodically so AttackFinishedPlayers and
            // similar config flags are respected after lock-on. Checked on an interval
            // rather than every physics step — the flags change rarely and this runs for
            // every drone in flight. Once dropped the target is not re-acquired; the
            // drone continues along its launch direction.
            if (_targetInfo != null)
            {
                _validateTimer -= Time.fixedDeltaTime;
                if (_validateTimer <= 0f)
                {
                    _validateTimer = TargetValidateInterval;
                    if (!IsValidTarget(_targetInfo))
                    {
                        IssaPluginPlugin.Log.LogInfo(
                            "[HunterDrone] Target is no longer valid — flying on without one."
                        );
                        _targetInfo = null;
                        _targetTransform = null;
                        _homingActive = false;
                    }
                }
            }

            UpdateHoming();
        }

        // ----------------------------------------------------------------
        //  Homing
        // ----------------------------------------------------------------

        private void UpdateHoming()
        {
            // Collision check (armed only). Uses a self-filtering helper so the
            // drone's own colliders — placed on HittablesLayer for bullet detection —
            // do not trigger a false detonation every frame.
            if (_armed && DetectExternalCollision())
            {
                IssaPluginPlugin.Log.LogInfo("[HunterDrone] Collision detected — detonating.");
                Detonate();
                return;
            }

            // Accelerate regardless of whether we have a target, capped at MaxSpeed.
            _currentSpeed = Mathf.Min(_currentSpeed + Acceleration * Time.fixedDeltaTime, MaxSpeed);

            Vector3 step;

            if (_targetTransform != null)
            {
                // Update aim point while actively tracking.
                if (_homingActive)
                {
                    float dist = Vector3.Distance(transform.position, _targetTransform.position);
                    if (HomingStopDistance > 0f && dist <= HomingStopDistance)
                        _homingActive = false;
                    else
                        _homingTarget = _targetTransform.position;
                }

                // Arrival check — only meaningful when we have a real target to home toward.
                Vector3 toTarget = _homingTarget - transform.position;
                float distToTarget = toTarget.magnitude;
                if (distToTarget < ArrivalRadius)
                {
                    Detonate();
                    return;
                }

                // Don't overshoot the aim point.
                float stepDist = Mathf.Min(_currentSpeed * Time.fixedDeltaTime, distToTarget);
                step = toTarget.normalized * stepDist;
            }
            else
            {
                // No target: fly in the player's aim direction (set at throw time) if available,
                // otherwise fall back to the drone's current facing direction.
                // No arrival check — only the collision sphere triggers detonation.
                Vector3 flyDir = _hasLaunchDirection ? _launchDirection : transform.forward;
                step = flyDir * _currentSpeed * Time.fixedDeltaTime;
            }

            _distanceTraveled += step.magnitude;
            if (_distanceTraveled >= MaxFlightDistance)
            {
                IssaPluginPlugin.Log.LogInfo(
                    $"[HunterDrone] Max flight distance ({MaxFlightDistance:F0} m) reached — detonating."
                );
                Detonate();
                return;
            }

            RotateTowardStep(step);
            _rb.MovePosition(transform.position + step);
        }

        /// <summary>
        /// Returns true if any collider within <see cref="ArrivalRadius"/> is an external object
        /// (i.e. not a collider belonging to this drone's own hierarchy).
        /// Using OverlapSphereNonAlloc + self-exclusion instead of CheckSphere prevents the drone
        /// from detecting its own physics colliders (which are on HittablesLayer, a layer included
        /// in RocketHittablesMask).
        /// </summary>
        private bool DetectExternalCollision()
        {
            int n = Physics.OverlapSphereNonAlloc(
                transform.position,
                ArrivalRadius,
                _collisionBuffer,
                GameManager.LayerSettings.RocketHittablesMask,
                QueryTriggerInteraction.Ignore
            );
            for (int i = 0; i < n; i++)
            {
                // Skip any collider that is part of this drone's own hierarchy.
                if (_collisionBuffer[i].transform.IsChildOf(transform))
                    continue;
                // Skip other hunter drones — drone-on-drone contact is not a detonation trigger.
                if (_collisionBuffer[i].GetComponentInParent<HunterDroneBehaviour>() != null)
                    continue;
                return true;
            }
            return false;
        }

        private bool IsValidTarget(PlayerInfo player) =>
            HunterDroneTargeting.IsValidTarget(
                player,
                ThrowerInfo,
                FriendlyFire,
                AttackFinishedPlayers
            );

        // ----------------------------------------------------------------
        //  Hit / shot-down handling (CustomHittable callback)
        // ----------------------------------------------------------------

        private void HandleHit()
        {
            if (!NetworkServer.active || _exploded)
                return;
            IssaPluginPlugin.Log.LogInfo("[HunterDrone] Shot down — detonating in place.");
            Detonate();
        }

        /// <summary>
        /// Called by <see cref="HunterDroneNetworkBridge"/> or
        /// <see cref="Patches.HunterDroneHitPatches"/> when a bullet hits this drone.
        /// Invokes the <see cref="CustomHittable.OnHit"/> pipeline, which triggers
        /// <see cref="HandleHit"/> and ultimately <see cref="Detonate"/>.
        /// </summary>
        public void ShootDown()
        {
            if (!NetworkServer.active || _exploded)
                return;
            OnHit?.Invoke();
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

            // Cap the Y so explosions from above don't drive the knockback downward.
            if (_targetTransform != null)
                pos.y = Mathf.Min(pos.y, _targetTransform.position.y + 0.5f);

            IssaPluginPlugin.Log.LogDebug($"[HunterDrone] Detonating at {pos:F1}.");

            NetworkServer.SendToAll(new HunterDroneExplodedMessage { Position = pos });

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

            NetworkServer.Destroy(gameObject);
        }

        // ----------------------------------------------------------------
        //  Rotation helper (same pattern as DroneBehaviour)
        // ----------------------------------------------------------------

        private void RotateTowardStep(Vector3 direction)
        {
            if (direction.sqrMagnitude < 0.0001f)
                return;

            Quaternion target = DirectionToRotation(direction.normalized);
            _rb.MoveRotation(
                Quaternion.RotateTowards(transform.rotation, target, 720f * Time.fixedDeltaTime)
            );
        }

        private static Quaternion DirectionToRotation(Vector3 direction)
        {
            Vector3 up =
                Mathf.Abs(Vector3.Dot(direction, Vector3.up)) > 0.99f
                    ? Vector3.forward
                    : Vector3.up;

            return Quaternion.LookRotation(direction, up);
        }
    }
}
