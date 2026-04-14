using System.Collections.Generic;
using System.Reflection;
using Mirror;
using UnityEngine;

namespace IssaPlugin.Items
{
    /// <summary>
    /// Server-only homing behaviour added to the hunter drone after NetworkServer.Spawn.
    ///
    /// Unlike DroneBehaviour (which has a circling phase), HunterDroneBehaviour skips
    /// straight to homing: it finds the closest valid enemy the moment it starts and
    /// flies directly at them.  Once within ArrivalRadius the drone detonates.
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
        public float ArrivalRadius = 2.5f;

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

        private Vector3 _fallbackAimPoint;
        private bool _hasFallbackAimPoint;

        // ----------------------------------------------------------------
        //  Internal state
        // ----------------------------------------------------------------

        private Rigidbody _rb;
        private Transform _targetTransform;
        private Vector3 _homingTarget;
        private bool _homingActive;
        private float _currentSpeed;
        private bool _armed;
        private float _armTimer;
        private bool _exploded;
        private bool _shotDown;

        // Safety: force-detonate if flight lasts longer than this.
        private const float FlightTimeout = 30f;
        private float _timeoutTimer;

        // How long to wait before retrying target selection when no valid targets exist.
        private const float NoTargetRetryInterval = 0.5f;
        private float _retryTimer;

        private static readonly MethodInfo ServerExplodeMethod = typeof(Rocket).GetMethod(
            "ServerExplode",
            BindingFlags.NonPublic | BindingFlags.Instance
        );

        // Reused across target-selection calls to avoid heap allocation.
        private static readonly List<(Transform t, float sqDist)> _candidateScratch = new();

        // ----------------------------------------------------------------
        //  Unity lifecycle
        // ----------------------------------------------------------------

        public void SetFallbackAimPoint(Vector3 worldPoint)
        {
            _fallbackAimPoint = worldPoint;
            _hasFallbackAimPoint = true;
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
            _timeoutTimer = FlightTimeout;

            // CustomHittable fields (hit once = destroyed).
            HitsRequired = 1;
            HitCount = 0;
            OnHit += HandleHit;

            // Select initial target immediately so the drone starts homing on spawn.
            TrySelectTarget();
        }

        private void FixedUpdate()
        {
            if (!NetworkServer.active || _exploded)
                return;

            _timeoutTimer -= Time.fixedDeltaTime;
            if (_timeoutTimer <= 0f)
            {
                IssaPluginPlugin.Log.LogInfo("[HunterDrone] Flight timed out — detonating.");
                Detonate();
                return;
            }

            // Arm delay: suppress collision detection briefly so the drone clears
            // the thrower before it starts checking for detonation triggers.
            if (!_armed)
            {
                _armTimer -= Time.fixedDeltaTime;
                if (_armTimer <= 0f)
                    _armed = true;
            }

            // Retry target selection if we don't have one yet.
            if (_targetTransform == null)
            {
                _retryTimer -= Time.fixedDeltaTime;
                if (_retryTimer <= 0f)
                {
                    TrySelectTarget();
                    _retryTimer = NoTargetRetryInterval;
                }
            }

            UpdateHoming();
        }

        // ----------------------------------------------------------------
        //  Homing
        // ----------------------------------------------------------------

        private void UpdateHoming()
        {
            // Collision check (armed only).
            if (
                _armed
                && Physics.CheckSphere(
                    transform.position,
                    ArrivalRadius,
                    GameManager.LayerSettings.RocketHittablesMask,
                    QueryTriggerInteraction.Ignore
                )
            )
            {
                IssaPluginPlugin.Log.LogInfo("[HunterDrone] Collision detected — detonating.");
                Detonate();
                return;
            }

            // Update aim point while actively tracking.
            if (_homingActive && _targetTransform != null)
            {
                float dist = Vector3.Distance(transform.position, _targetTransform.position);
                bool stopHoming = HomingStopDistance > 0f && dist <= HomingStopDistance;
                if (!stopHoming)
                    _homingTarget = _targetTransform.position;
                else
                    _homingActive = false;
            }

            // Arrival check before moving to avoid silent overshoot.
            float distToTarget = Vector3.Distance(transform.position, _homingTarget);
            if (distToTarget < ArrivalRadius)
            {
                Detonate();
                return;
            }

            // Accelerate.
            _currentSpeed += Acceleration * Time.fixedDeltaTime;

            Vector3 dir;
            if (_homingActive && _targetTransform != null)
            {
                dir = _homingTarget - transform.position;
            }
            else if (_hasFallbackAimPoint)
            {
                dir = _fallbackAimPoint - transform.position;
                if (_armed && dir.magnitude < ArrivalRadius)
                {
                    Detonate();
                    return;
                }
            }
            else
            {
                dir = transform.forward;
            }

            Vector3 step = dir.normalized * _currentSpeed * Time.fixedDeltaTime;
            if (step.magnitude > distToTarget)
                step = dir;

            RotateTowardStep(step);
            _rb.MovePosition(transform.position + step);
        }

        // ----------------------------------------------------------------
        //  Target selection — picks the CLOSEST valid enemy
        // ----------------------------------------------------------------

        private void TrySelectTarget()
        {
            _candidateScratch.Clear();

            var local = GameManager.LocalPlayerInfo;
            if (local != null && IsValidTarget(local))
            {
                float sq = (local.transform.position - transform.position).sqrMagnitude;
                _candidateScratch.Add((local.transform, sq));
            }

            var remotes = GameManager.RemotePlayers;
            if (remotes != null)
            {
                foreach (var p in remotes)
                {
                    if (!IsValidTarget(p))
                        continue;
                    float sq = (p.transform.position - transform.position).sqrMagnitude;
                    _candidateScratch.Add((p.transform, sq));
                }
            }

            if (_candidateScratch.Count == 0)
                return;

            // Pick the closest.
            int bestIdx = 0;
            for (int i = 1; i < _candidateScratch.Count; i++)
                if (_candidateScratch[i].sqDist < _candidateScratch[bestIdx].sqDist)
                    bestIdx = i;

            _targetTransform = _candidateScratch[bestIdx].t;
            _homingTarget = _targetTransform.position;
            _homingActive = true;

            IssaPluginPlugin.Log.LogInfo(
                $"[HunterDrone] Locked on to {_targetTransform.name} at "
                    + $"{_targetTransform.position:F1}."
            );
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
        //  Hit / shot-down handling (CustomHittable callback)
        // ----------------------------------------------------------------

        private void HandleHit()
        {
            if (!NetworkServer.active || _exploded || _shotDown)
                return;
            if (HitsRequired <= 0)
                return;
            if (HitCount >= HitsRequired)
                return;

            HitCount++;
            if (HitCount >= HitsRequired)
            {
                _shotDown = true;
                IssaPluginPlugin.Log.LogInfo("[HunterDrone] Shot down — detonating in place.");
                Detonate();
            }
        }

        /// <summary>
        /// Called by <see cref="HunterDroneNetworkBridge"/> or
        /// <see cref="Patches.HunterDroneHitPatches"/> when a bullet hits this drone.
        /// Invokes the <see cref="CustomHittable.OnHit"/> pipeline, which triggers
        /// <see cref="HandleHit"/> and ultimately <see cref="Detonate"/>.
        /// </summary>
        public void ShootDown()
        {
            if (!NetworkServer.active || _exploded || _shotDown)
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

            IssaPluginPlugin.Log.LogInfo($"[HunterDrone] Detonating at {pos:F1}.");

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
