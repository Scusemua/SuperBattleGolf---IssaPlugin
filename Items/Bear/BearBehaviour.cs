using System.Collections.Generic;
using Mirror;
using UnityEngine;

namespace IssaPlugin.Items
{
    /// <summary>
    /// Attached server-side to each spawned bear GameObject immediately after
    /// NetworkServer.Spawn(). Drives the full AI loop:
    ///
    ///   FixedUpdate: decrement state timer → UpdateStateMachine (which calls
    ///   per-state movement and transition logic).
    ///
    /// All physics and game-state mutations happen here on the server.
    /// Clients receive BearStateMessage on state changes and follow via NetworkTransform.
    ///
    /// This component is disabled on clients (Start() checks NetworkServer.active).
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class BearBehaviour : MonoBehaviour
    {
        // ── Set by BearNetworkBridge immediately after AddComponent ───────────

        /// The PlayerInfo of the player who summoned this bear (used for kill credit).
        public PlayerInfo SummonerInfo;

        /// Shared reference to this bear's hit receiver — needed to wire up the
        /// aggro notification when the bear is hit.
        public BearHitReceiver HitReceiver;

        // ── Internal references ───────────────────────────────────────────────

        private Rigidbody _rb;
        private Animator _animator;
        private BearAnimatorDriver _animatorDriver;
        private BearTargetSelector _selector;
        private NetworkIdentity _ni;

        // ── AI state ──────────────────────────────────────────────────────────

        private BearAIState _state = BearAIState.Spawning;
        private float _stateTimer;

        private PlayerInfo _currentTarget;

        // Attack tracking
        private bool _attackHitApplied; // prevent double-hit per attack swing
        private float _attackHitWindow; // time within attack animation where hit fires

        // Wander state (used during Idle)
        private Vector3 _wanderTarget;
        private float _wanderTimer;

        // Obstacle avoidance context steering
        private const int ContextRayCount = 8;
        private const float AngleStep = 360f / ContextRayCount;
        private readonly float[] _interest = new float[ContextRayCount];
        private readonly float[] _danger = new float[ContextRayCount];
        private Vector3 _cachedSteeringDir = Vector3.forward;
        private int _steeringTick;

        // Layer masks (cached)
        private int _obstacleMask;

        // Destroy guard — prevents repeated NetworkServer.Destroy calls in Dead state
        private bool _destroying;

        // Set by BlackHoleGrenadeBehaviour each physics tick while this bear is in
        // suction range.  Prevents MoveInDirection from overwriting the applied force.
        private float _blackHoleSuppressedUntil;

        // ── Kinematic movement ────────────────────────────────────────────────
        // The bear uses a kinematic Rigidbody during normal AI states so it blocks
        // players without imparting the velocity-based forces that a dynamic body
        // would. We switch to dynamic only for physics-driven states (Stunned, Dying,
        // Dead, black-hole suction) so AddForce still works for knockback.

        /// XZ velocity commanded by MoveInDirection this tick; applied by ApplyKinematicPosition.
        private Vector3 _kinematicVelocity;

        /// Manually accumulated Y velocity (simulates gravity while kinematic).
        private float _yVelocity;

        /// Reusable raycast buffer — avoids per-frame heap allocation.
        private static readonly RaycastHit[] _groundHitBuffer = new RaycastHit[8];

        // ── Unity lifecycle ───────────────────────────────────────────────────

        private void Start()
        {
            // Bears only simulate on the server
            if (!NetworkServer.active)
            {
                enabled = false;
                return;
            }

            _rb = GetComponent<Rigidbody>();
            _animator = GetComponent<Animator>();
            _animatorDriver = GetComponent<BearAnimatorDriver>();
            _ni = GetComponent<NetworkIdentity>();
            _selector = new BearTargetSelector();

            _obstacleMask = LayerMask.GetMask("Default", "Terrain");

            // Rigidbody setup: kinematic by default so the bear acts as a solid
            // blocker without imparting its velocity onto players via physics.
            // We switch to dynamic in UpdateKinematicMode() for physics-driven
            // states (Stunned, Dying, Dead, black-hole suction).
            _rb.isKinematic = true;
            _rb.useGravity = false;
            _rb.freezeRotation = true;
            _rb.interpolation = RigidbodyInterpolation.Interpolate;

            // Aggro notification is handled directly via OnHitByExplosion —
            // no separate event subscription needed.

            // Start in Spawning state
            TransitionTo(BearAIState.Spawning);
        }

        private void FixedUpdate()
        {
            if (!NetworkServer.active)
                return;

            _stateTimer -= Time.fixedDeltaTime;

            UpdateKinematicMode();
            UpdateStateMachine();
            ApplyKinematicPosition();
        }

        /// <summary>
        /// Switches the Rigidbody between kinematic and dynamic based on the current
        /// AI state and whether a black hole is active.
        ///
        ///   Kinematic: normal movement states — bear is a solid blocker that cannot
        ///              impart its velocity onto players as a collision force.
        ///   Dynamic:   Stunned / Dying / Dead / black-hole — AddForce must work for
        ///              melee knockback, explosion impulses, and suction physics.
        /// </summary>
        private void UpdateKinematicMode()
        {
            bool needsDynamic =
                Time.fixedTime < _blackHoleSuppressedUntil
                || _state == BearAIState.Stunned
                || _state == BearAIState.Dying
                || _state == BearAIState.Dead;

            if (_rb.isKinematic == needsDynamic) // need to flip
            {
                if (needsDynamic)
                {
                    // kinematic → dynamic: hand off manual Y velocity to physics
                    _rb.isKinematic = false;
                    _rb.useGravity = true;
                    _rb.linearVelocity = new Vector3(0f, _yVelocity, 0f);
                }
                else
                {
                    // dynamic → kinematic: capture current Y so we continue smoothly
                    _yVelocity = _rb.linearVelocity.y;
                    _rb.linearVelocity = Vector3.zero;
                    _rb.isKinematic = true;
                    _rb.useGravity = false;
                }
            }
        }

        /// <summary>
        /// Applies the XZ velocity set by MoveInDirection plus manual gravity to the
        /// kinematic Rigidbody in a single MovePosition call per physics tick.
        /// A downward raycast snaps the bear to the ground when grounded so it
        /// doesn't hover above uneven terrain.
        /// Self-colliders are filtered from the raycast results so the bear never
        /// treats its own collider as the ground surface.
        /// No-op when the Rigidbody is dynamic (physics handles it directly).
        /// </summary>
        private void ApplyKinematicPosition()
        {
            if (!_rb.isKinematic)
                return;

            // Accumulate gravity
            _yVelocity += Physics.gravity.y * Time.fixedDeltaTime;

            // Ground check — filter out the bear's own colliders
            float targetY = _rb.position.y + _yVelocity * Time.fixedDeltaTime;

            Vector3 origin = _rb.position + Vector3.up;
            int hitCount = Physics.RaycastNonAlloc(
                origin,
                Vector3.down,
                _groundHitBuffer,
                1.5f,
                _obstacleMask
            );

            float bestDist = float.MaxValue;
            for (int i = 0; i < hitCount; i++)
            {
                ref RaycastHit h = ref _groundHitBuffer[i];
                if (h.collider.transform.IsChildOf(transform))
                    continue;
                if (h.distance < bestDist)
                {
                    bestDist = h.distance;
                    if (_rb.position.y <= h.point.y + 0.05f)
                    {
                        targetY = h.point.y;
                        if (_yVelocity < 0f)
                            _yVelocity = 0f;
                    }
                }
            }

            _rb.MovePosition(
                new Vector3(
                    _rb.position.x + _kinematicVelocity.x * Time.fixedDeltaTime,
                    targetY,
                    _rb.position.z + _kinematicVelocity.z * Time.fixedDeltaTime
                )
            );
            _kinematicVelocity = Vector3.zero;
        }

        // ── State machine ─────────────────────────────────────────────────────

        private void UpdateStateMachine()
        {
            // While a black hole is actively pulling this bear, suppress all AI
            // velocity writes so the applied force can move the bear freely.
            if (Time.fixedTime < _blackHoleSuppressedUntil)
                return;

            switch (_state)
            {
                case BearAIState.Spawning:
                    // Stand still during spawn animation, then start hunting
                    if (_stateTimer <= 0f)
                        TransitionTo(BearAIState.Idle);
                    break;

                case BearAIState.Idle:
                    UpdateIdle();
                    break;

                case BearAIState.Wander:
                    UpdateWander();
                    break;

                case BearAIState.Pursuing:
                    UpdatePursuing();
                    break;

                case BearAIState.Charging:
                    UpdateCharging();
                    break;

                case BearAIState.Attacking:
                    UpdateAttacking();
                    break;

                case BearAIState.AttackCooldown:
                    if (_stateTimer <= 0f)
                        TransitionTo(BearAIState.Pursuing);
                    break;

                case BearAIState.Stunned:
                    // No movement while stunned; transition to enraged on recovery
                    if (_stateTimer <= 0f)
                        TransitionTo(BearAIState.Enraged);
                    break;

                case BearAIState.Enraged:
                    UpdateEnraged();
                    break;

                case BearAIState.Dying:
                    if (_stateTimer <= 0f)
                        TransitionTo(BearAIState.Dead);
                    break;

                case BearAIState.Dead:
                    // Guard prevents repeated Destroy calls across multiple FixedUpdate
                    // ticks before Unity processes the pending destroy.
                    if (!_destroying)
                    {
                        _destroying = true;
                        NetworkServer.Destroy(gameObject);
                    }
                    break;
            }
        }

        // ── Per-state update methods ──────────────────────────────────────────

        private void UpdateIdle()
        {
            _currentTarget = _selector.SelectTarget(transform.position, GetTargetablePlayers());

            if (_currentTarget != null)
                TransitionTo(BearAIState.Pursuing);
            else
                TransitionTo(BearAIState.Wander); // triggers IsWalking on clients
        }

        private void UpdateWander()
        {
            _currentTarget = _selector.SelectTarget(transform.position, GetTargetablePlayers());

            if (_currentTarget != null)
            {
                TransitionTo(BearAIState.Pursuing);
                return;
            }

            DoWander();
        }

        private void UpdatePursuing()
        {
            _currentTarget = _selector.SelectTarget(transform.position, GetTargetablePlayers());

            if (_currentTarget == null)
            {
                TransitionTo(BearAIState.Idle);
                return;
            }

            float dist = Vector3.Distance(transform.position, _currentTarget.transform.position);

            // Close enough to commit to a charge
            if (dist <= Configuration.BearChargeRange.Value)
            {
                TransitionTo(BearAIState.Charging);
                return;
            }

            // Target may be on an elevated platform we cannot reach
            if (!IsTargetReachable(_currentTarget.transform.position))
            {
                DoObstructedBehavior();
                return;
            }

            // Normal pursuit with obstacle avoidance
            Vector3 desiredDir = GetDesiredDirection(_currentTarget.transform.position);
            Vector3 steeredDir = ComputeContextSteering(desiredDir);
            MoveInDirection(steeredDir, Configuration.BearRunSpeed.Value);
        }

        private void UpdateCharging()
        {
            if (_currentTarget == null)
            {
                TransitionTo(BearAIState.Pursuing);
                return;
            }

            float dist = Vector3.Distance(transform.position, _currentTarget.transform.position);

            // Within attack range — start swing
            if (dist <= Configuration.BearAttackRange.Value)
            {
                TransitionTo(BearAIState.Attacking);
                return;
            }

            // Charge: committed straight-line sprint, no obstacle steering
            // (gives the bear that scary committed feel — it will go over things)
            Vector3 dir = GetDesiredDirection(_currentTarget.transform.position);
            MoveInDirection(dir, Configuration.BearChargeSpeed.Value);
        }

        private void UpdateAttacking()
        {
            // Apply hit at the midpoint of the attack animation (configurable window)
            if (!_attackHitApplied && _stateTimer <= _attackHitWindow)
            {
                _attackHitApplied = true;

                // Re-check distance in case target moved
                if (_currentTarget != null)
                {
                    float dist = Vector3.Distance(
                        transform.position,
                        _currentTarget.transform.position
                    );

                    // Slightly generous hit range to compensate for server tick lag
                    if (dist <= Configuration.BearAttackRange.Value * 1.4f)
                        ApplyAttackHit(_currentTarget);
                }
            }

            if (_stateTimer <= 0f)
                TransitionTo(BearAIState.AttackCooldown);
        }

        private void UpdateEnraged()
        {
            if (_stateTimer <= 0f)
            {
                TransitionTo(BearAIState.Pursuing);
                return;
            }

            // Enraged: same as pursuing but faster, re-uses same target lock
            _currentTarget = _selector.SelectTarget(transform.position, GetTargetablePlayers());

            if (_currentTarget == null)
            {
                TransitionTo(BearAIState.Idle);
                return;
            }

            float dist = Vector3.Distance(transform.position, _currentTarget.transform.position);
            if (dist <= Configuration.BearChargeRange.Value)
            {
                TransitionTo(BearAIState.Charging);
                return;
            }

            Vector3 desiredDir = GetDesiredDirection(_currentTarget.transform.position);
            Vector3 steeredDir = ComputeContextSteering(desiredDir);
            float enrageSpd =
                Configuration.BearRunSpeed.Value * Configuration.BearEnrageSpeedMultiplier.Value;
            MoveInDirection(steeredDir, enrageSpd);
        }

        // ── Navigation helpers ────────────────────────────────────────────────

        /// <summary>
        /// Context steering: scores 8 candidate directions around the bear.
        /// Directions containing obstacles are zeroed out; the highest-scoring
        /// remaining direction wins. This gives natural obstacle avoidance without
        /// a NavMesh or pathfinding graph.
        /// </summary>
        private Vector3 ComputeContextSteering(Vector3 desiredWorldDir)
        {
            // Recompute every 3 fixed ticks; reuse cached result in between.
            // At 50 Hz this still re-steers ~17 times/second, imperceptible to players.
            _steeringTick++;
            if (_steeringTick % 3 != 0)
                return _cachedSteeringDir;

            const float RayLength = 4.5f;
            const float RayOriginLift = 0.6f; // raise origin so we don't hit ground

            for (int i = 0; i < ContextRayCount; i++)
            {
                float angle = i * AngleStep;
                Vector3 dir = Quaternion.Euler(0f, angle, 0f) * Vector3.forward;

                // Interest: alignment with desired direction
                _interest[i] = Mathf.Max(0f, Vector3.Dot(dir, desiredWorldDir));

                // Danger: obstacle in this direction
                Vector3 origin = transform.position + Vector3.up * RayOriginLift;
                _danger[i] = Physics.Raycast(origin, dir, RayLength, _obstacleMask) ? 1f : 0f;
            }

            // Mask out dangerous directions
            for (int i = 0; i < ContextRayCount; i++)
                if (_danger[i] > 0f)
                    _interest[i] = 0f;

            // Pick best
            int best = 0;
            for (int i = 1; i < ContextRayCount; i++)
                if (_interest[i] > _interest[best])
                    best = i;

            // All directions blocked — try least-dangerous fallback
            if (_interest[best] <= 0f)
            {
                _cachedSteeringDir = GetLeastDangerousDirection();
                return _cachedSteeringDir;
            }

            float bestAngle = best * AngleStep;
            _cachedSteeringDir = (Quaternion.Euler(0f, bestAngle, 0f) * Vector3.forward).normalized;
            return _cachedSteeringDir;
        }

        private Vector3 GetLeastDangerousDirection()
        {
            int leastDanger = 0;
            float leastDangerVal = float.MaxValue;

            for (int i = 0; i < ContextRayCount; i++)
            {
                if (_danger[i] < leastDangerVal)
                {
                    leastDangerVal = _danger[i];
                    leastDanger = i;
                }
            }

            float angle = leastDanger * AngleStep;
            return (Quaternion.Euler(0f, angle, 0f) * Vector3.forward).normalized;
        }

        /// <summary>
        /// Returns the normalised horizontal world-space direction toward a target position.
        /// </summary>
        private Vector3 GetDesiredDirection(Vector3 targetPos)
        {
            Vector3 toTarget = targetPos - transform.position;
            toTarget.y = 0f;
            return toTarget.sqrMagnitude > 0.001f ? toTarget.normalized : transform.forward;
        }

        /// <summary>
        /// Orients the bear toward <paramref name="worldDir"/> and stores the desired
        /// XZ velocity for this tick. The actual position update is deferred to
        /// <see cref="ApplyKinematicPosition"/> so the single MovePosition call combines
        /// XZ movement with the gravity/ground-snap Y update.
        /// </summary>
        private void MoveInDirection(Vector3 worldDir, float speed)
        {
            if (worldDir.sqrMagnitude > 0.01f)
            {
                Quaternion targetRot = Quaternion.LookRotation(worldDir, Vector3.up);
                transform.rotation = Quaternion.Slerp(
                    transform.rotation,
                    targetRot,
                    Configuration.BearTurnSpeed.Value * Time.fixedDeltaTime
                );
            }

            _kinematicVelocity = worldDir * speed;
        }

        /// <summary>
        /// Simple wander: pick a random nearby point, walk to it, repeat.
        /// </summary>
        private void DoWander()
        {
            _wanderTimer -= Time.fixedDeltaTime;

            if (_wanderTimer <= 0f || Vector3.Distance(transform.position, _wanderTarget) < 2f)
            {
                Vector2 rand = Random.insideUnitCircle * Configuration.BearWanderRadius.Value;
                _wanderTarget = transform.position + new Vector3(rand.x, 0f, rand.y);
                _wanderTimer = Random.Range(3f, 8f);
            }

            Vector3 dir = GetDesiredDirection(_wanderTarget);
            MoveInDirection(dir, Configuration.BearWalkSpeed.Value);
        }

        /// <summary>
        /// Called when the bear can see a target but cannot reach them (elevation gap).
        /// Bear paces and growls at the base of the obstacle.
        /// </summary>
        private void DoObstructedBehavior()
        {
            // Slow pace back and forth while growling (uses Idle Combat animation via state)
            // We move very slowly toward the target's XZ projection so the bear circles
            // the base of the obstacle naturally.
            if (_currentTarget == null)
                return;

            Vector3 flatTarget = new Vector3(
                _currentTarget.transform.position.x,
                transform.position.y,
                _currentTarget.transform.position.z
            );

            Vector3 dir = GetDesiredDirection(flatTarget);
            Vector3 steered = ComputeContextSteering(dir);
            MoveInDirection(steered, Configuration.BearWalkSpeed.Value * 0.6f);
        }

        /// <summary>
        /// Returns true if the target position is reachable from the bear's
        /// current position — i.e. not on a ledge or platform far above/below.
        /// </summary>
        private bool IsTargetReachable(Vector3 targetPos)
        {
            // Only check height — a direct ray toward the target always hits hillside
            // slopes and incorrectly marks them as unreachable. Context steering in
            // ComputeContextSteering handles true walls and cliff faces.
            float heightDiff = targetPos.y - transform.position.y;
            return Mathf.Abs(heightDiff) <= Configuration.BearMaxClimbHeight.Value;
        }

        // ── Attack ────────────────────────────────────────────────────────────

        private void ApplyAttackHit(PlayerInfo target)
        {
            if (target == null || SummonerInfo == null)
                return;

            var hittable = target.GetComponentInChildren<Hittable>();
            if (hittable == null)
                return;

            // Direction of knockback: away from the bear
            Vector3 knockDir = (target.transform.position - transform.position).normalized;
            // Add a slight upward angle for the "thrown" feel
            knockDir = (knockDir + Vector3.up * 0.4f).normalized;

            Vector3 localHitPoint = hittable.transform.InverseTransformPoint(
                target.transform.position
            );

            // Reuse the ElephantGun hit type — produces the ragdoll knockback you want
            var useId = new ItemUseId(
                SummonerInfo.PlayerId.Guid,
                BearItem.NextUseIndex(),
                ItemType.ElephantGun
            );

            hittable.HitWithItem(
                ItemType.ElephantGun,
                useId,
                localHitPoint,
                knockDir,
                hittable.transform.InverseTransformPoint(transform.position), // "barrel" end
                Vector3.Distance(transform.position, target.transform.position),
                SummonerInfo.Inventory,
                false,
                false,
                false
            );

            // Broadcast impact event for client-side sound / camera shake
            NetworkServer.SendToAll(
                new BearAttackImpactMessage
                {
                    BearNetId = _ni.netId,
                    ImpactPosition = target.transform.position,
                }
            );

            IssaPluginPlugin.Log.LogInfo(
                $"[Bear] Attack hit applied to {target.PlayerId.PlayerName}."
            );

            // Notify the summoner's client so they can see the bear is doing work.
            // Use BearNetworkBridge (a NetworkBehaviour) — the canonical, hierarchy-safe
            // way to reach a player's connection in this codebase.
            var summConn = SummonerInfo.GetComponent<BearNetworkBridge>()?.connectionToClient;
            summConn?.Send(
                new HitNotificationMessage { Message = $"Bear hit {target.PlayerId.PlayerName}!" }
            );
        }

        // ── External events ───────────────────────────────────────────────────

        /// <summary>
        /// Called by GolfClubBearHitPatch when the bear is struck by a melee swing.
        /// Applies an impulse to the rigidbody (giving a "flying" feel) and transitions
        /// to Stunned so the AI does not fight the physics impulse.
        /// </summary>
        public void ApplyMeleeKnockback(Vector3 direction, float force)
        {
            if (_rb == null || _destroying)
                return;

            // AddForce requires a dynamic Rigidbody. TransitionTo(Stunned) has already
            // been called before this method, but UpdateKinematicMode won't run until
            // the next FixedUpdate, so we flip it immediately here.
            if (_rb.isKinematic)
            {
                _rb.isKinematic = false;
                _rb.useGravity = true;
                _rb.linearVelocity = new Vector3(0f, _yVelocity, 0f);
            }

            _rb.AddForce(direction * force, ForceMode.Impulse);
        }

        /// <summary>
        /// Called by BearHitReceiver when the bear is struck by an explosion / rocket.
        /// Transitions to Stunned state and notifies the target selector for aggro.
        /// </summary>
        public void OnHitByExplosion(PlayerInfo attacker)
        {
            // Already dying — ignore
            if (_state == BearAIState.Dying || _state == BearAIState.Dead)
                return;

            _selector.NotifyHitBy(attacker);
            TransitionTo(BearAIState.Stunned);

            IssaPluginPlugin.Log.LogInfo("[Bear] Bear stunned by explosion.");
        }

        /// <summary>
        /// Called each physics tick by BlackHoleGrenadeBehaviour while this bear is
        /// within suction range.  Extends the AI suppression window so MoveInDirection
        /// does not overwrite the suction force applied to the Rigidbody.
        /// </summary>
        public void NotifyBlackHoleSuction()
        {
            // Two fixed ticks ahead prevents a gap if the black hole's FixedUpdate
            // runs just before ours in the same frame.
            _blackHoleSuppressedUntil = Time.fixedTime + Time.fixedDeltaTime * 2f;

            // The black hole calls AddForce on our Rigidbody BEFORE calling this method,
            // so we must flip to dynamic immediately — UpdateKinematicMode won't run
            // until our next FixedUpdate.
            if (_rb != null && _rb.isKinematic)
            {
                _rb.isKinematic = false;
                _rb.useGravity = true;
                _rb.linearVelocity = new Vector3(0f, _yVelocity, 0f);
            }
        }

        /// <summary>
        /// Called by BlackHoleGrenadeBehaviour immediately before applying the spit
        /// velocity.  Suppresses AI movement for long enough that the bear actually
        /// flies rather than having its velocity overwritten on the next FixedUpdate.
        /// </summary>
        public void NotifyBlackHoleSpitLaunch()
        {
            _blackHoleSuppressedUntil = Time.fixedTime + 2f;
        }

        /// <summary>
        /// Called by BearHitReceiver when the bear's HP reaches zero.
        /// </summary>
        public void OnKilled()
        {
            if (_state == BearAIState.Dying || _state == BearAIState.Dead)
                return;
            TransitionTo(BearAIState.Dying);

            IssaPluginPlugin.Log.LogInfo("[Bear] Bear killed.");
        }

        // ── State transition ──────────────────────────────────────────────────

        private void TransitionTo(BearAIState newState)
        {
            _state = newState;

            switch (newState)
            {
                case BearAIState.Spawning:
                    _stateTimer = Configuration.BearSpawnAnimationDuration.Value;
                    ZeroVelocity();
                    break;

                case BearAIState.Idle:
                    _stateTimer = 0f;
                    _wanderTimer = 0f; // pick new wander target immediately
                    break;

                case BearAIState.Wander:
                    _stateTimer = 0f;
                    break;

                case BearAIState.Pursuing:
                    _stateTimer = 0f;
                    break;

                case BearAIState.Charging:
                    _stateTimer = 0f;
                    break;

                case BearAIState.Attacking:
                    float attackDur = Configuration.BearAttackAnimationDuration.Value;
                    _stateTimer = attackDur;
                    _attackHitApplied = false;
                    // Hit fires at 55% through the animation (claw impact moment)
                    _attackHitWindow = attackDur * 0.45f;
                    ZeroVelocity();
                    break;

                case BearAIState.AttackCooldown:
                    _stateTimer = Configuration.BearAttackCooldown.Value;
                    _currentTarget = null;
                    ZeroVelocity();
                    break;

                case BearAIState.Stunned:
                    _stateTimer = Configuration.BearStunDuration.Value;
                    ZeroVelocity();
                    break;

                case BearAIState.Enraged:
                    _stateTimer = Configuration.BearEnrageDuration.Value;
                    // Force target re-evaluation after stun
                    _selector.ClearLock();
                    break;

                case BearAIState.Dying:
                    _stateTimer = Configuration.BearDeathAnimationDuration.Value;
                    ZeroVelocity();
                    // Disable collisions so dead bear doesn't block players
                    foreach (var col in GetComponents<Collider>())
                        col.enabled = false;
                    break;

                case BearAIState.Dead:
                    _stateTimer = 0f;
                    break;
            }

            // Drive the server-side Animator so root motion plays the correct clip.
            // Clients receive their own update via BroadcastState → HandleBearState.
            _animatorDriver?.ApplyState(
                newState,
                _currentTarget != null ? _currentTarget.transform.position : Vector3.zero
            );

            // Notify all clients of the state change
            BroadcastState();
        }

        private void BroadcastState()
        {
            NetworkServer.SendToAll(
                new BearStateMessage
                {
                    BearNetId = _ni != null ? _ni.netId : 0u,
                    State = _state,
                    TargetPosition =
                        _currentTarget != null ? _currentTarget.transform.position : Vector3.zero,
                }
            );
        }

        private void ZeroVelocity()
        {
            _kinematicVelocity = Vector3.zero;
            _yVelocity = 0f;
            if (_rb != null && !_rb.isKinematic)
            {
                _rb.linearVelocity = Vector3.zero;
                _rb.angularVelocity = Vector3.zero;
            }
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static readonly List<PlayerInfo> _playerScratchpad = [];
        private static readonly List<PlayerInfo> _targetableScratchpad = [];

        private static List<PlayerInfo> GetActivePlayers()
        {
            // Reuse a static scratch list to avoid per-FixedUpdate allocations.
            // Safe because FixedUpdate runs single-threaded on the Unity main thread.
            _playerScratchpad.Clear();

            if (GameManager.LocalPlayerInfo != null)
                _playerScratchpad.Add(GameManager.LocalPlayerInfo);

            var remotes = GameManager.RemotePlayers;
            if (remotes != null)
                _playerScratchpad.AddRange(remotes);

            return _playerScratchpad;
        }

        /// <summary>
        /// Returns the list of players that this bear is allowed to target.
        /// When <see cref="Configuration.BearFriendlyFire"/> is false, the
        /// summoner is excluded so bears never turn on the player who deployed them.
        /// </summary>
        private List<PlayerInfo> GetTargetablePlayers()
        {
            var all = GetActivePlayers();
            if (Configuration.BearFriendlyFire.Value || SummonerInfo == null)
                return all;

            _targetableScratchpad.Clear();
            foreach (var p in all)
                if (p != SummonerInfo)
                    _targetableScratchpad.Add(p);
            return _targetableScratchpad;
        }
    }
}
