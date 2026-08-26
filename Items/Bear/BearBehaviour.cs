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

        /// Physics ticks between obstacle rescans. Interest/blending still runs
        /// every tick, so tracking stays smooth; only the raycasts are throttled.
        private const int SteeringRescanInterval = 6;

        /// Per-bear phase offset so a pack staggers its rescans across ticks
        /// instead of spiking together.
        private int _steeringPhase;

        /// Fraction of full speed the bear retains while turned directly away from
        /// its commanded direction. Keeps it creeping forward through a hard pivot
        /// instead of rooting in place and spinning.
        private const float MinTurnThrottle = 0.45f;

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

        // ── Ground probing (mirrors PlayerMovement.TryFindGround) ─────────────
        // The bear follows terrain the same way the player does: a downward ray
        // from the collider centre, rejected if the surface pitch exceeds the
        // player's ungroundable threshold, with a ring of fallback rays for
        // ledges the centre ray misses, and extra probe distance while already
        // grounded so slopes and small drops don't cause a spurious unground.

        /// Half-height of the bear's collider — probe origin lift and snap offset.
        private float _capsuleHalfHeight = 1f;

        /// Radius used for the fallback grounding ray ring.
        private float _capsuleRadius = 0.5f;

        /// True when the last probe found walkable ground.
        private bool _isGrounded;

        /// Surface point/normal of the ground under the bear (valid when grounded).
        private Vector3 _groundPoint;
        private Vector3 _groundNormal = Vector3.up;

        /// Max walkable surface pitch in degrees; read from the player's settings
        /// so the bear can climb exactly what a player can climb.
        private float _slopeLimitDeg = 55f;

        /// Extra downward probe distance while grounded (keeps the bear stuck to
        /// slopes and small steps instead of launching off them).
        private float _groundProbeAddition = 0.5f;

        /// Layer mask for groundable surfaces — matches the player's mask.
        private int _groundMask;

        /// Number of fallback rays in the grounding ring.
        private const int GroundRingRayCount = 4;

        // ── Movement diagnostics (opt-in via config) ─────────────────────────

        private Vector3 _lastCommandedPos;
        private Vector3 _lastActualPos;
        private bool _hasCommandedPos;
        private float _diagTimer;
        private float _diagDriftSum;
        private float _diagIntendedSum;
        private Vector3 _diagWindowStartPos;

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

            if (_animator != null)
            {
                // AI drives position from FixedUpdate, so the Animator must not
                // also write the transform.
                _animator.applyRootMotion = false;

                // Keep the Animator on the physics clock. Sampling animation on
                // render frames while the body steps in FixedUpdate makes the
                // bear appear to slide and stutter relative to its own legs.
                _animator.updateMode = AnimatorUpdateMode.Fixed;
            }

            // Stagger this bear's steering scans against its packmates.
            _steeringPhase = Random.Range(0, SteeringRescanInterval);

            InitializeGroundProbeSettings();

            // Rigidbody setup: kinematic by default so the bear acts as a solid
            // blocker without imparting its velocity onto players via physics.
            // We switch to dynamic in UpdateKinematicMode() for physics-driven
            // states (Stunned, Dying, Dead, black-hole suction).
            _rb.isKinematic = true;
            _rb.useGravity = false;
            _rb.interpolation = RigidbodyInterpolation.Interpolate;

            // Clear any position freezes baked into the prefab and keep only the
            // rotation lock we actually want. A prefab with Freeze Position X/Z
            // ticked silently defeats MovePosition on those axes — the bear would
            // turn to face its target but never travel. Setting the mask
            // explicitly makes this immune to prefab drift.
            if (_rb.constraints != RigidbodyConstraints.FreezeRotation)
            {
                IssaPluginPlugin.Log.LogInfo(
                    $"[Bear] Overriding Rigidbody constraints {_rb.constraints} "
                        + "→ FreezeRotation (position axes must stay free)."
                );
            }
            _rb.constraints = RigidbodyConstraints.FreezeRotation;

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
        /// <summary>
        /// Reads the bear's collider dimensions and copies the player's grounding
        /// tuning so the bear can traverse exactly what a player can traverse.
        /// Falls back to sane defaults if the game settings aren't reachable.
        /// </summary>
        private void InitializeGroundProbeSettings()
        {
            // Derive probe geometry from the bear's own collider so the values
            // stay correct if the prefab is rescaled.
            var capsule = GetComponentInChildren<CapsuleCollider>();
            if (capsule != null)
            {
                float scale = Mathf.Max(
                    transform.lossyScale.x,
                    transform.lossyScale.y,
                    transform.lossyScale.z
                );
                _capsuleHalfHeight = Mathf.Max(0.1f, capsule.height * 0.5f * scale);
                _capsuleRadius = Mathf.Max(0.05f, capsule.radius * scale);
            }
            else
            {
                var anyCollider = GetComponentInChildren<Collider>();
                if (anyCollider != null)
                {
                    Vector3 extents = anyCollider.bounds.extents;
                    _capsuleHalfHeight = Mathf.Max(0.1f, extents.y);
                    _capsuleRadius = Mathf.Max(0.05f, Mathf.Min(extents.x, extents.z));
                }
            }

            // Mirror the player's grounding tuning. Wrapped defensively because
            // GameManager settings may not be initialized in every context.
            try
            {
                var settings = GameManager.PlayerMovementSettings;
                if (settings != null)
                {
                    _slopeLimitDeg = settings.UngroundableGroundPitchThreshold;
                    _groundProbeAddition = settings.GroundCheckDistanceAdditionWhenGrounded;
                }

                var layers = GameManager.LayerSettings;
                if (layers != null)
                    _groundMask = layers.PlayerGroundableMask;
            }
            catch (System.Exception ex)
            {
                IssaPluginPlugin.Log.LogWarning(
                    $"[Bear] Could not read player movement settings, using defaults: {ex.Message}"
                );
            }

            if (_groundMask == 0)
                _groundMask = _obstacleMask;

            IssaPluginPlugin.Log.LogDebug(
                $"[Bear] Ground probe: halfHeight={_capsuleHalfHeight:F2} "
                    + $"radius={_capsuleRadius:F2} slopeLimit={_slopeLimitDeg:F1}° "
                    + $"probeAddition={_groundProbeAddition:F2} mask={_groundMask}"
            );

            // One-time inventory of everything on the bear, so any component that
            // also writes the transform (NetworkTransform, third-party movement)
            // is visible in the log instead of having to be guessed at.
            if (ModConfig.Bear.LogMovementDiagnostics.Value)
            {
                var names = new List<string>();
                foreach (var c in GetComponents<Component>())
                    if (c != null)
                        names.Add(c.GetType().Name);

                IssaPluginPlugin.Log.LogInfo(
                    $"[Bear] Root components: {string.Join(", ", names)}"
                );
                IssaPluginPlugin.Log.LogInfo(
                    $"[Bear] RB: kinematic={_rb.isKinematic} gravity={_rb.useGravity} "
                        + $"constraints={_rb.constraints} interpolation={_rb.interpolation} "
                        + $"animatorUpdateMode={(_animator != null ? _animator.updateMode.ToString() : "n/a")}"
                );
            }
        }

        /// <summary>
        /// Probes for walkable ground beneath the bear, mirroring
        /// PlayerMovement.TryFindGround: a centre ray from the collider midpoint,
        /// surface-pitch rejection so steep faces aren't treated as floor, and a
        /// ring of fallback rays for ledges/edges the centre ray misses.
        /// While already grounded the probe reaches further down so slopes and
        /// small drops keep the bear attached instead of launching it.
        /// </summary>
        private bool TryFindGround(out Vector3 point, out Vector3 normal)
        {
            point = Vector3.zero;
            normal = Vector3.up;

            // Reach below the feet: a little always, plus more while grounded so
            // downhill slopes and steps stay attached.
            float probeBelowFeet = 0.2f + (_isGrounded && _yVelocity <= 1f ? _groundProbeAddition : 0f);
            float maxDistance = _capsuleHalfHeight + probeBelowFeet;

            Vector3 centre = _rb.position + Vector3.up * _capsuleHalfHeight;
            bool found = false;
            float bestDist = float.PositiveInfinity;

            if (CastGroundRay(centre, maxDistance, ref bestDist, ref point, ref normal))
                found = true;

            // Centre ray found nothing walkable — sweep a ring of offset rays so
            // the bear still grounds when straddling an edge or a narrow ridge.
            if (!found)
            {
                for (int i = 0; i < GroundRingRayCount; i++)
                {
                    float angle = Mathf.PI * 2f * i / GroundRingRayCount;
                    Vector3 offset =
                        new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle)) * _capsuleRadius;
                    if (CastGroundRay(centre + offset, maxDistance, ref bestDist, ref point, ref normal))
                        found = true;
                }
            }

            return found;
        }

        /// <summary>
        /// Casts one downward grounding ray, ignoring the bear's own colliders and
        /// rejecting surfaces steeper than the walkable slope limit. Keeps the
        /// nearest qualifying hit found so far.
        /// </summary>
        private bool CastGroundRay(
            Vector3 origin,
            float maxDistance,
            ref float bestDist,
            ref Vector3 point,
            ref Vector3 normal
        )
        {
            int hitCount = Physics.RaycastNonAlloc(
                origin,
                Vector3.down,
                _groundHitBuffer,
                maxDistance,
                _groundMask,
                QueryTriggerInteraction.Ignore
            );

            bool found = false;
            for (int i = 0; i < hitCount; i++)
            {
                ref RaycastHit h = ref _groundHitBuffer[i];

                // Never ground on ourselves.
                if (h.collider.transform.IsChildOf(transform))
                    continue;

                // Reject surfaces too steep to walk on — these are walls/cliffs,
                // not floor. This is what lets the bear climb ramps but not walls.
                if (Vector3.Angle(h.normal, Vector3.up) > _slopeLimitDeg)
                    continue;

                if (h.distance < bestDist)
                {
                    bestDist = h.distance;
                    point = h.point;
                    normal = h.normal;
                    found = true;
                }
            }

            return found;
        }

        /// <summary>
        /// Applies the XZ velocity set by MoveInDirection plus gravity to the
        /// kinematic Rigidbody in a single MovePosition call per physics tick.
        ///
        /// When grounded, the destination is projected onto the ground plane
        /// (the same trick PlayerMovement.GetNextFrameGroundPoint uses) so the
        /// bear follows slopes up and down smoothly instead of walking into
        /// hillsides or stepping off into the air. Airborne, it falls under
        /// gravity until the probe reacquires ground.
        /// No-op when the Rigidbody is dynamic (physics handles it directly).
        /// </summary>
        private void ApplyKinematicPosition()
        {
            if (!_rb.isKinematic)
                return;

            Vector3 groundPoint;
            Vector3 groundNormal;
            _isGrounded = TryFindGround(out groundPoint, out groundNormal);
            _groundPoint = groundPoint;
            _groundNormal = groundNormal;

            // Horizontal step for this tick.
            Vector3 horizontalStep = new Vector3(
                _kinematicVelocity.x * Time.fixedDeltaTime,
                0f,
                _kinematicVelocity.z * Time.fixedDeltaTime
            );

            Vector3 nextPos = _rb.position + horizontalStep;

            if (_isGrounded)
            {
                // Cancel accumulated fall speed — we're standing on something.
                if (_yVelocity < 0f)
                    _yVelocity = 0f;

                // Project the destination onto the ground plane so uphill and
                // downhill movement both track the surface. Guard against a
                // near-vertical normal producing an extreme projection.
                float denom = groundNormal.y;
                if (denom > 0.01f)
                {
                    float planeY =
                        groundPoint.y
                        - (
                            groundNormal.x * (nextPos.x - groundPoint.x)
                            + groundNormal.z * (nextPos.z - groundPoint.z)
                        ) / denom;

                    // Clamp how far a single tick may lift or drop the bear so a
                    // bad probe can never teleport it.
                    float maxStep = _capsuleHalfHeight + _groundProbeAddition;
                    nextPos.y = Mathf.Clamp(
                        planeY,
                        _rb.position.y - maxStep,
                        _rb.position.y + maxStep
                    );
                }
                else
                {
                    nextPos.y = groundPoint.y;
                }
            }
            else
            {
                // Airborne — accumulate gravity and fall.
                _yVelocity += Physics.gravity.y * Time.fixedDeltaTime;
                nextPos.y = _rb.position.y + _yVelocity * Time.fixedDeltaTime;
            }

            // Write the transform directly rather than going through MovePosition.
            //
            // MovePosition on a kinematic body performs a swept move and lets
            // PhysX refuse or clamp it against anything the capsule touches.
            // Bears spawn as a cluster around the player, so they overlap each
            // other immediately and each one's sweep is blocked by its siblings —
            // the AI commands full speed, PhysX grants almost none of it, and the
            // bear rotates on the spot. Setting position directly makes the AI
            // authoritative over movement; obstacle avoidance is already handled
            // by the context-steering rays, not by collision response.
            //
            // Rigidbody.position (not transform.position) keeps the body and its
            // collider in sync for the next query without a full SyncTransforms.
            _rb.position = nextPos;
            transform.position = nextPos;

            // Diagnostic: compares what we asked for against what actually stuck.
            // A large, persistent gap means something outside this component is
            // also writing the transform (root motion, a NetworkTransform in the
            // wrong authority mode, or a collider wedged in geometry).
            if (ModConfig.Bear.LogMovementDiagnostics.Value)
                LogMovementDelta(nextPos);

            _kinematicVelocity = Vector3.zero;
        }

        /// <summary>
        /// Reports, once per second, the difference between the position this
        /// component commanded last tick and the position the bear actually
        /// occupies now. Intended for diagnosing "bear animates but doesn't move".
        /// </summary>
        private void LogMovementDelta(Vector3 commandedPos)
        {
            // Seed the window baseline on the first tick, otherwise the first
            // report measures against Vector3.zero and prints a meaningless
            // world-origin-sized distance.
            if (!_hasCommandedPos)
                _diagWindowStartPos = _rb.position;

            _diagTimer += Time.fixedDeltaTime;

            if (_hasCommandedPos)
            {
                // How much of last tick's commanded position failed to stick.
                // Now that position is written directly this should stay ~0;
                // a persistent non-zero value means something else moved the bear.
                Vector3 drift = _rb.position - _lastCommandedPos;
                drift.y = 0f;
                _diagDriftSum += drift.magnitude;

                // Distance the AI asked for this tick.
                Vector3 intended = _kinematicVelocity * Time.fixedDeltaTime;
                intended.y = 0f;
                _diagIntendedSum += intended.magnitude;
            }

            if (_diagTimer >= 1f)
            {
                // Net straight-line progress over the last second. This is the
                // number that separates the failure modes:
                //   intended high, net ~0, lost high  → another writer is winning
                //   intended high, net ~0, lost ~0    → moved then oscillated back
                //   intended ~0                       → AI never commanded movement
                Vector3 net = _rb.position - _diagWindowStartPos;
                net.y = 0f;

                IssaPluginPlugin.Log.LogInfo(
                    $"[Bear] state={_state} grounded={_isGrounded} "
                        + $"intendedXZ/s={_diagIntendedSum:F2} netXZ/s={net.magnitude:F2} "
                        + $"lostToOtherWriters/s={_diagDriftSum:F2} "
                        + $"kinematic={_rb.isKinematic} "
                        + $"rootMotion={(_animator != null && _animator.applyRootMotion)}"
                );
                _diagTimer = 0f;
                _diagDriftSum = 0f;
                _diagIntendedSum = 0f;
                _diagWindowStartPos = _rb.position;
            }

            _lastActualPos = _rb.position;
            _lastCommandedPos = commandedPos;
            _hasCommandedPos = true;
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
            if (dist <= ModConfig.Bear.ChargeRange.Value)
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
            MoveInDirection(steeredDir, ModConfig.Bear.RunSpeed.Value);
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
            if (dist <= ModConfig.Bear.AttackRange.Value)
            {
                TransitionTo(BearAIState.Attacking);
                return;
            }

            // Charge: committed straight-line sprint, no obstacle steering
            // (gives the bear that scary committed feel — it will go over things)
            Vector3 dir = GetDesiredDirection(_currentTarget.transform.position);
            MoveInDirection(dir, ModConfig.Bear.ChargeSpeed.Value);
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
                    if (dist <= ModConfig.Bear.AttackRange.Value * 1.4f)
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
            if (dist <= ModConfig.Bear.ChargeRange.Value)
            {
                TransitionTo(BearAIState.Charging);
                return;
            }

            Vector3 desiredDir = GetDesiredDirection(_currentTarget.transform.position);
            Vector3 steeredDir = ComputeContextSteering(desiredDir);
            float enrageSpd =
                ModConfig.Bear.RunSpeed.Value * ModConfig.Bear.EnrageSpeedMultiplier.Value;
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
            const float RayLength = 4.5f;

            // The obstacle scan is the expensive part, so it runs every 3 fixed
            // ticks (~17 Hz) and the danger values are reused in between. The
            // interest/blend below still runs every tick, so the steered
            // direction tracks a moving target continuously — caching the final
            // direction instead made the bear chase a stale heading and circle
            // rather than close on a player who was moving around it.
            // Rescan obstacles every SteeringRescanInterval ticks. The phase
            // offset is per-bear so a pack doesn't run all its scans on the same
            // physics tick — that bunching is what turns 5 bears into a periodic
            // frame spike rather than a flat, small cost.
            _steeringTick++;
            bool rescanObstacles =
                (_steeringTick + _steeringPhase) % SteeringRescanInterval == 0;

            // Probe from around chest height. Anything below this the bear can
            // simply walk over, so sampling low would flag every bump as a wall.
            float rayOriginLift = _capsuleHalfHeight;

            for (int i = 0; i < ContextRayCount; i++)
            {
                float angle = i * AngleStep;
                Vector3 dir = Quaternion.Euler(0f, angle, 0f) * Vector3.forward;

                // Interest: alignment with desired direction. Recomputed every
                // tick so the bear tracks a moving target without lag.
                // Raised to a power so the heading nearest the target dominates
                // the blend — a flat dot product spreads weight too evenly and
                // pulls the bear sideways.
                float alignment = Mathf.Max(0f, Vector3.Dot(dir, desiredWorldDir));
                _interest[i] = alignment * alignment;

                if (!rescanObstacles)
                    continue;

                // Danger: only surfaces too steep to walk up count as obstacles.
                // A hillside the bear can climb must NOT be treated as a wall,
                // otherwise every direction gets masked out on sloped terrain and
                // the bear wanders aimlessly instead of pursuing.
                Vector3 origin = transform.position + Vector3.up * rayOriginLift;
                _danger[i] = 0f;

                RaycastHit hit;
                if (
                    Physics.Raycast(
                        origin,
                        dir,
                        out hit,
                        RayLength,
                        _obstacleMask,
                        QueryTriggerInteraction.Ignore
                    )
                )
                {
                    if (hit.collider.transform.IsChildOf(transform))
                        continue;

                    // Walkable slope → not a danger. Steep face → blocked.
                    if (Vector3.Angle(hit.normal, Vector3.up) > _slopeLimitDeg)
                    {
                        // Scale danger by proximity so the bear prefers directions
                        // with more clearance rather than treating all walls alike.
                        _danger[i] = Mathf.Clamp01(1f - hit.distance / RayLength);
                    }
                }
            }

            // Penalise dangerous directions rather than hard-masking them, so a
            // distant wall only discourages a direction instead of eliminating it.
            // Anything very close is still fully blocked.
            for (int i = 0; i < ContextRayCount; i++)
            {
                if (_danger[i] >= 0.6f)
                    _interest[i] = 0f;
                else
                    _interest[i] *= 1f - _danger[i];
            }

            // Blend the candidate directions weighted by interest rather than
            // snapping to the single best one. Picking a winner quantised the
            // output to 45° buckets, so a circling player made the steered
            // direction jump between buckets — the bear kept re-orienting toward
            // a target that never settled and made little forward progress.
            // A weighted sum yields a continuous direction that tracks smoothly.
            Vector3 blended = Vector3.zero;
            float totalWeight = 0f;
            for (int i = 0; i < ContextRayCount; i++)
            {
                if (_interest[i] <= 0f)
                    continue;

                float angle = i * AngleStep;
                Vector3 dir = Quaternion.Euler(0f, angle, 0f) * Vector3.forward;
                blended += dir * _interest[i];
                totalWeight += _interest[i];
            }

            // Every direction blocked — fall back to the least-dangerous one.
            if (totalWeight <= 0f || blended.sqrMagnitude < 0.0001f)
            {
                _cachedSteeringDir = GetLeastDangerousDirection();
                return _cachedSteeringDir;
            }

            _cachedSteeringDir = blended.normalized;
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
            if (worldDir.sqrMagnitude <= 0.01f)
            {
                _kinematicVelocity = Vector3.zero;
                return;
            }

            worldDir = worldDir.normalized;

            Quaternion targetRot = Quaternion.LookRotation(worldDir, Vector3.up);
            transform.rotation = Quaternion.Slerp(
                transform.rotation,
                targetRot,
                ModConfig.Bear.TurnSpeed.Value * Time.fixedDeltaTime
            );

            // Travel along the direction the bear is actually facing, not the raw
            // commanded direction. Moving sideways relative to the body looked
            // like sliding, and when a player circled the bear the commanded
            // direction swung far enough each tick that successive steps largely
            // cancelled — the bear spun without getting anywhere.
            Vector3 facing = transform.forward;
            facing.y = 0f;
            if (facing.sqrMagnitude < 0.0001f)
                facing = worldDir;
            else
                facing.Normalize();

            // Ease off the throttle only while sharply mis-aimed, so the bear
            // pivots to face a target beside it instead of arcing wide, then
            // returns to full speed as it comes around. Never drops to a full
            // stop — a bear that stands still while turning reads as broken.
            float alignment = Vector3.Dot(facing, worldDir); // -1..1
            float throttle = Mathf.Lerp(MinTurnThrottle, 1f, Mathf.InverseLerp(-1f, 1f, alignment));

            _kinematicVelocity = facing * (speed * throttle);
        }

        /// <summary>
        /// Simple wander: pick a random nearby point, walk to it, repeat.
        /// </summary>
        private void DoWander()
        {
            _wanderTimer -= Time.fixedDeltaTime;

            if (_wanderTimer <= 0f || Vector3.Distance(transform.position, _wanderTarget) < 2f)
            {
                Vector2 rand = Random.insideUnitCircle * ModConfig.Bear.WanderRadius.Value;
                _wanderTarget = transform.position + new Vector3(rand.x, 0f, rand.y);
                _wanderTimer = Random.Range(3f, 8f);
            }

            Vector3 dir = GetDesiredDirection(_wanderTarget);
            MoveInDirection(dir, ModConfig.Bear.WalkSpeed.Value);
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
            MoveInDirection(steered, ModConfig.Bear.WalkSpeed.Value * 0.6f);
        }

        /// <summary>
        /// Returns true if the target position is reachable from the bear's
        /// current position — i.e. not on a ledge or platform far above/below.
        /// </summary>
        private bool IsTargetReachable(Vector3 targetPos)
        {
            float heightDiff = targetPos.y - transform.position.y;

            // Anything within the plain climb budget is reachable.
            if (Mathf.Abs(heightDiff) <= ModConfig.Bear.MaxClimbHeight.Value)
                return true;

            // Above the budget, the height alone doesn't decide it — a target at
            // the top of a long walkable ramp IS reachable, while one on a sheer
            // ledge is not. Sample the surface partway toward the target and
            // treat it as reachable if the bear could walk up that gradient.
            Vector3 toTarget = targetPos - transform.position;
            toTarget.y = 0f;
            float flatDist = toTarget.magnitude;
            if (flatDist < 0.5f)
                return false; // directly overhead — no ramp to walk

            // Gradient the bear would have to average to get there. If that is
            // within the walkable slope limit, a path plausibly exists.
            float requiredAngle = Mathf.Atan2(Mathf.Abs(heightDiff), flatDist) * Mathf.Rad2Deg;
            return requiredAngle <= _slopeLimitDeg;
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
                ItemType.ElephantGun,
                false
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
                false,
                NetworkTime.time,
                0UL
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
                    _stateTimer = ModConfig.Bear.SpawnAnimationDuration.Value;
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
                    float attackDur = ModConfig.Bear.AttackAnimationDuration.Value;
                    _stateTimer = attackDur;
                    _attackHitApplied = false;
                    // Hit fires at 55% through the animation (claw impact moment)
                    _attackHitWindow = attackDur * 0.45f;
                    ZeroVelocity();
                    break;

                case BearAIState.AttackCooldown:
                    _stateTimer = ModConfig.Bear.AttackCooldown.Value;
                    _currentTarget = null;
                    ZeroVelocity();
                    break;

                case BearAIState.Stunned:
                    _stateTimer = ModConfig.Bear.StunDuration.Value;
                    ZeroVelocity();
                    break;

                case BearAIState.Enraged:
                    _stateTimer = ModConfig.Bear.EnrageDuration.Value;
                    // Force target re-evaluation after stun
                    _selector.ClearLock();
                    break;

                case BearAIState.Dying:
                    _stateTimer = ModConfig.Bear.DeathAnimationDuration.Value;
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
        /// When <see cref="ModConfig.Bear.FriendlyFire"/> is false, the
        /// summoner is excluded so bears never turn on the player who deployed them.
        /// </summary>
        private List<PlayerInfo> GetTargetablePlayers()
        {
            var all = GetActivePlayers();
            if (ModConfig.Bear.FriendlyFire.Value || SummonerInfo == null)
                return all;

            _targetableScratchpad.Clear();
            foreach (var p in all)
                if (p != SummonerInfo)
                    _targetableScratchpad.Add(p);
            return _targetableScratchpad;
        }
    }
}
