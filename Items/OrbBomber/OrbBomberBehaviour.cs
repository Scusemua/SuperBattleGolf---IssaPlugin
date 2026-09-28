using System.Collections.Generic;
using System.Reflection;
using Mirror;
using UnityEngine;

namespace IssaPlugin.Items
{
    /// Server-only chase. Planted on the ground, stops to grow, and can be flung
    /// by the victim's swing. Follows the victim, or their golf ball when
    /// ChaseGolfBall is enabled. Remote clients only see the transform plus the
    /// baked <see cref="OrbBomberClientSetup"/> visuals.
    public class OrbBomberBehaviour : MonoBehaviour
    {
        public const float ImpactIgnoreSeconds = 0.35f;
        public const float MinImpactSpeed = 3.5f;
        public const float SwingOverlapRadius = 1.75f;
        public const float SwingProbeRadius = SwingOverlapRadius + 3f;

        private const float SpinPerImpulse = 0.55f;
        private const float ExplosionForce = 15f;
        private const float BulletKnockSpeed = 10f;
        private const float FlungDrag = 1.6f;
        private const float CartMinSpeed = 2.5f;
        private const float CartKnockScale = 1.35f;

        private enum Phase
        {
            Approach,
            Detonating,
            Flung,
        }

        public PlayerInfo ThrowerInfo;
        public PlayerInfo TargetInfo;
        public uint VictimNetId;
        public ItemUseId ItemUseId;

        private OrbBomberClientSetup _setup;
        private Rigidbody _rb;
        private Phase _phase = Phase.Approach;
        private bool _finished;
        private float _spawnTime;
        private float _plantedCenterY;
        private float _sequenceStart;
        private float _sequenceDuration;
        private float _ignoreUntil;
        private bool _leftGround;
        private bool _impactArmed;
        private bool _pendingFastContact;
        private float _blackHoleSuppressedUntil;
        private float _cartKnockCooldown;
        private float _swingLockUntil;
        private bool _mustLeaveRange;
        private bool _reapplyKnock;
        private Vector3 _knockVelocity;
        private Vector3 _knockSpin;
        private float _angerBonus;
        private float _bulletLockUntil;
        private int _groundMask;

        private static readonly RaycastHit[] GroundHits = new RaycastHit[32];
        private static readonly Collider[] CartOverlap = new Collider[16];
        private static readonly Dictionary<int, Vector3> CartPositions = new Dictionary<int, Vector3>();
        private static readonly Dictionary<int, Vector3> CartVelocities = new Dictionary<int, Vector3>();
        private static readonly List<int> CartPrune = new List<int>();
        private static float _cartSampleTime = -1f;

        private static readonly MethodInfo ServerExplodeMethod = typeof(Rocket).GetMethod(
            "ServerExplode",
            BindingFlags.NonPublic | BindingFlags.Instance
        );

        private void Start()
        {
            _setup = GetComponent<OrbBomberClientSetup>();
            _rb = GetComponent<Rigidbody>();
            if (_rb == null)
                _rb = gameObject.AddComponent<Rigidbody>();

            _rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            _rb.interpolation = RigidbodyInterpolation.None;
            EnterApproachBody();
            _spawnTime = Time.time;

            var layers = GameManager.LayerSettings;
            if (layers != null)
                _groundMask = layers.PlayerGroundableMask;
        }

        private void FixedUpdate()
        {
            if (!NetworkServer.active || _finished)
                return;

            if (_rb == null || _setup == null)
            {
                Despawn();
                return;
            }

            if (Time.time - _spawnTime >= Mathf.Max(1f, ModConfig.OrbBomber.MaxLifetime.Value))
            {
                Despawn();
                return;
            }

            var chaseTarget = ChaseTransform(TargetInfo);
            if (chaseTarget == null)
            {
                Despawn();
                return;
            }

            if (Time.fixedTime < _blackHoleSuppressedUntil)
            {
                // A knock from before the grab would otherwise replay over the
                // suction once the window ends.
                _reapplyKnock = false;
                return;
            }

            if (_reapplyKnock && _rb != null && !_rb.isKinematic)
            {
                // The detonation tick may already have queued a kinematic move
                // this frame. Put the knock back after that, or the countdown
                // stays planted and the swing looks like it did nothing.
                _rb.linearVelocity = _knockVelocity;
                _rb.angularVelocity = _knockSpin;
                _reapplyKnock = false;
            }

            TryCartKnock();

            switch (_phase)
            {
                case Phase.Approach:
                    TickApproach(chaseTarget.position);
                    break;
                case Phase.Detonating:
                    TickDetonation(chaseTarget.position);
                    break;
                case Phase.Flung:
                    TickFlung();
                    break;
            }
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (!NetworkServer.active || _finished)
                return;

            if (
                _rb != null
                && _rb.isKinematic
                && collision.collider != null
                && collision.collider.GetComponentInParent<GolfCartInfo>() is GolfCartInfo cart
            )
            {
                SampleCarts();
                if (CartVelocities.TryGetValue(cart.GetInstanceID(), out Vector3 velocity))
                    ApplyCartKnock(velocity);
            }

            if (_phase != Phase.Flung)
                return;
            if (Time.time < _ignoreUntil)
                return;
            if (collision.relativeVelocity.magnitude >= MinImpactSpeed)
                _pendingFastContact = true;
        }

        /// Client-owned carts often move by transform sync, so a kinematic orb never
        /// receives a physics push. Sample cart motion and fling on overlap instead.
        private void TryCartKnock()
        {
            if (_rb == null || _setup == null || Time.time < _cartKnockCooldown)
                return;

            SampleCarts();

            float radius =
                _setup.BodyRadius * Mathf.Max(transform.localScale.x, 1f) + 0.75f;
            int count = Physics.OverlapSphereNonAlloc(
                _rb.position,
                radius,
                CartOverlap,
                Physics.AllLayers,
                QueryTriggerInteraction.Ignore
            );

            for (int i = 0; i < count; i++)
            {
                var col = CartOverlap[i];
                if (col == null)
                    continue;
                if (col.transform == transform || col.transform.IsChildOf(transform))
                    continue;

                var cart = col.GetComponentInParent<GolfCartInfo>();
                if (cart == null)
                    continue;
                if (!CartVelocities.TryGetValue(cart.GetInstanceID(), out Vector3 velocity))
                    continue;

                var cartBody = cart.GetComponent<Rigidbody>() ?? cart.GetComponentInChildren<Rigidbody>();
                bool cartSimulated =
                    cartBody != null && !cartBody.isKinematic && cartBody.linearVelocity.sqrMagnitude >= 1f;
                // A simulated cart pushes a dynamic body on its own once the
                // chase has stopped writing velocity. Keep scanning other hits.
                if (_phase != Phase.Approach && !_rb.isKinematic && cartSimulated)
                    continue;

                if (ApplyCartKnock(velocity))
                    return;
            }
        }

        private static void SampleCarts()
        {
            if (Mathf.Approximately(_cartSampleTime, Time.fixedTime))
                return;
            _cartSampleTime = Time.fixedTime;

            CartPrune.Clear();
            foreach (var id in CartPositions.Keys)
                CartPrune.Add(id);

            float dt = Mathf.Max(Time.fixedDeltaTime, 0.0001f);
            var carts = Object.FindObjectsByType<GolfCartInfo>(FindObjectsSortMode.None);
            foreach (var cart in carts)
            {
                if (cart == null)
                    continue;

                int id = cart.GetInstanceID();
                CartPrune.Remove(id);
                Vector3 pos = cart.transform.position;
                var body = cart.GetComponent<Rigidbody>() ?? cart.GetComponentInChildren<Rigidbody>();
                Vector3 velocity = body != null ? body.linearVelocity : Vector3.zero;
                if (velocity.sqrMagnitude < 1f && CartPositions.TryGetValue(id, out Vector3 previous))
                    velocity = (pos - previous) / dt;

                CartPositions[id] = pos;
                CartVelocities[id] = velocity;
            }

            foreach (int id in CartPrune)
            {
                CartPositions.Remove(id);
                CartVelocities.Remove(id);
            }
        }

        private bool ApplyCartKnock(Vector3 cartVelocity)
        {
            if (_finished || _rb == null || _setup == null)
                return false;
            if (Time.time < _cartKnockCooldown)
                return false;
            if (cartVelocity.sqrMagnitude < CartMinSpeed * CartMinSpeed)
                return false;

            Vector3 direction = cartVelocity.normalized;
            float speed = cartVelocity.magnitude * CartKnockScale;
            Fling(
                direction * speed + Vector3.up * Mathf.Clamp(speed * 0.15f, 0.5f, 3f),
                TumbleSpin(direction, speed)
            );
            _cartKnockCooldown = Time.time + 0.45f;
            return true;
        }

        public void ServerHandleSwing(PlayerInfo swinger)
        {
            if (!NetworkServer.active || _finished || swinger == null || _setup == null || _rb == null)
                return;
            if (Time.time < _swingLockUntil)
                return;

            var swingerIdentity = swinger.GetComponent<NetworkIdentity>();
            if (swingerIdentity == null)
                return;

            float bodyRadius = _setup.BodyRadius * Mathf.Max(transform.localScale.x, 1f);
            Vector3 probeCenter = swinger.transform.position;
            float probeRadius = SwingProbeRadius;
            if (swinger.AsGolfer != null)
                GetSwingProbe(swinger.AsGolfer, out probeCenter, out probeRadius);

            // Reach is measured from the swing box, so a giant's long reach still
            // counts and a swing from across the hole does not.
            float reach = probeRadius + bodyRadius + 2f;
            if ((probeCenter - transform.position).sqrMagnitude > reach * reach)
                return;

            bool isVictim = swingerIdentity.netId == VictimNetId;
            _swingLockUntil = Time.time + 0.5f;

            Vector3 knockDir = SwingLaunchDirection(swinger);
            float force =
                Mathf.Max(0f, ModConfig.OrbBomber.ClubKnockbackForce.Value)
                * SwingPowerScale(swinger)
                * SwingForceMultiplier(swinger);

            // Velocity is written directly. AddForce is ignored on the frame a
            // kinematic body, such as a detonating orb, becomes dynamic.
            Fling(knockDir * force, TumbleSpin(knockDir, force));
            if (isVictim)
            {
                _impactArmed =
                    Random.value < Mathf.Clamp01(ModConfig.OrbBomber.ImpactExplodeChance.Value);
            }
        }

        /// Black hole suction already called AddForce. Stay dynamic and let that force stick.
        public void NotifyBlackHoleSuction() => YieldToBlackHole(Time.fixedDeltaTime * 2f);

        /// Called just before the black hole writes the spit velocity.
        public void NotifyBlackHoleSpitLaunch() => YieldToBlackHole(2f);

        private void YieldToBlackHole(float seconds)
        {
            if (_finished || _rb == null)
                return;

            bool firstDisrupt = _phase != Phase.Flung;
            _blackHoleSuppressedUntil = Time.fixedTime + seconds;
            _reapplyKnock = false;
            if (_phase == Phase.Detonating)
                _mustLeaveRange = true;
            InterruptDetonationVisual();
            ReleaseToPhysics(clearVelocity: false);
            _phase = Phase.Flung;
            if (firstDisrupt)
                AddAnger();
        }

        public void ApplyExplosion(Vector3 origin, float radius, float scale)
        {
            if (_finished || _rb == null || _setup == null)
                return;

            float scaledRadius = Mathf.Max(0.5f, radius);
            Vector3 center = _rb.worldCenterOfMass;
            Vector3 away = center - origin;
            if (away.sqrMagnitude < 0.0001f)
                away = Vector3.up;
            away.Normalize();

            float dist = Vector3.Distance(center, origin);
            float falloff = 1f - Mathf.Clamp01(dist / scaledRadius);
            float speed = ExplosionForce * Mathf.Max(1f, scale) * Mathf.Lerp(0.35f, 1f, falloff);
            Vector3 knockDir = (away + Vector3.up * 0.35f).normalized;
            Fling(knockDir * speed, TumbleSpin(knockDir, speed));
        }

        public void ApplyFirearmHit(Vector3 shotDirection)
        {
            if (_finished || _rb == null || _setup == null)
                return;
            if (Time.time < _bulletLockUntil)
                return;

            _bulletLockUntil = Time.time + 0.03f;
            if (shotDirection.sqrMagnitude < 0.0001f)
                shotDirection = transform.forward;
            shotDirection.Normalize();

            Vector3 knockDir = (shotDirection + Vector3.up * 0.35f).normalized;
            Fling(knockDir * BulletKnockSpeed, TumbleSpin(knockDir, BulletKnockSpeed));
        }

        public static void ServerHandleBulletMessage(uint orbNetId, Vector3 direction)
        {
            if (!NetworkServer.active)
                return;
            if (!NetworkServer.spawned.TryGetValue(orbNetId, out var identity))
                return;

            identity.GetComponent<OrbBomberBehaviour>()?.ApplyFirearmHit(direction);
        }

        private void Fling(Vector3 velocity, Vector3 spin)
        {
            if (_phase == Phase.Detonating)
                _mustLeaveRange = true;

            InterruptDetonationVisual();
            AddAnger();
            _phase = Phase.Flung;
            ReleaseToPhysics(clearVelocity: true);
            _knockVelocity = velocity;
            _knockSpin = spin;
            _rb.linearVelocity = velocity;
            _rb.angularVelocity = spin;
            _reapplyKnock = true;
            _ignoreUntil = Time.time + ImpactIgnoreSeconds;
            _leftGround = false;
            _pendingFastContact = false;
        }

        private void AddAnger()
        {
            _angerBonus += Mathf.Max(0f, ModConfig.OrbBomber.AngerSpeedBonus.Value);
        }

        /// Matches the club's swing box, including a Jumbo Burger giant and the
        /// extra reach Super Jumbo Burger adds on top of that.
        internal static void GetSwingProbe(PlayerGolfer golfer, out Vector3 worldCenter, out float radius)
        {
            var info = golfer.PlayerInfo;
            bool giant = info != null && info.IsInJumboBurgerGiantForm;
            Vector3 localCenter = GameManager.GolfSettings.SwingHitBoxLocalCenter;
            float cover = SwingProbeRadius;
            if (giant && GameManager.ItemSettings != null)
            {
                localCenter = GameManager.ItemSettings.JumboBurgerSwingHitBoxLocalCenter;
                Vector3 size = GameManager.ItemSettings.JumboBurgerSwingHitBoxSize;
                cover = Mathf.Max(cover, 0.5f * size.magnitude);
            }

            Vector3 playerPosition = golfer.transform.position;
            worldCenter = golfer.transform.TransformPoint(localCenter);
            float multiplier = 1f;
            if (giant && info.Movement != null)
            {
                multiplier = SuperJumboBurgerBehaviour.GetFlickHitboxMultiplier(
                    info.Movement.CharacterScale
                );
            }

            if (multiplier > 1f)
                worldCenter = playerPosition + (worldCenter - playerPosition) * multiplier;

            radius = cover * Mathf.Max(1f, multiplier);
        }

        internal static void GetSwingHitWindow(PlayerGolfer golfer, out float start, out float end)
        {
            var settings = GameManager.GolfSettings;
            bool giant = golfer.PlayerInfo != null && golfer.PlayerInfo.IsInJumboBurgerGiantForm;
            if (giant)
            {
                start = settings.GetSwingHitStartTime(SwingType.JumboBurgerGiant);
                end = settings.GetSwingHitEndTime(SwingType.JumboBurgerGiant);
                return;
            }

            start = settings.SwingHitStartTime;
            end = settings.SwingHitEndTime;
        }

        /// Same construction as PlayerGolfer.GetSwingDirection: facing, pitched
        /// up by the club loft. Pitch 0 stays flat, like a putt.
        private static Vector3 SwingLaunchDirection(PlayerInfo swinger)
        {
            var golfer = swinger.AsGolfer;
            Vector3 forward = swinger.transform.forward;
            if (forward.sqrMagnitude < 0.0001f)
                forward = Vector3.forward;
            if (golfer == null)
                return forward.normalized;

            return (
                Quaternion.AngleAxis(-golfer.SwingPitch, swinger.transform.right) * forward
            ).normalized;
        }

        private static float SwingPowerScale(PlayerInfo swinger)
        {
            var golfer = swinger.AsGolfer;
            if (golfer == null)
                return 1f;
            return Mathf.Max(0.5f, golfer.SwingNormalizedPower);
        }

        private static float SwingForceMultiplier(PlayerInfo swinger)
        {
            float multiplier = 1f;
            var inventory = swinger.GetComponent<PlayerInventory>();
            if (
                inventory != null
                && inventory.GetEffectivelyEquippedItem(true) == ItemRegistry.BaseballBatItemType
            )
            {
                multiplier *= Mathf.Max(1f, ModConfig.BaseballBat.PowerMultiplier.Value);
            }

            if (swinger.GetComponent<SpinachNetworkBridge>() is { ServerIsBuffActive: true })
                multiplier *= Mathf.Max(1f, ModConfig.Spinach.PowerMultiplier.Value);

            return multiplier;
        }

        private void InterruptDetonationVisual()
        {
            if (_phase != Phase.Detonating || _setup == null)
                return;

            _setup.ResetSequence();
            Vector3 planted = _rb.position;
            planted.y = _plantedCenterY;
            _rb.position = planted;

            var identity = GetComponent<NetworkIdentity>();
            if (identity == null)
                return;

            NetworkServer.SendToAll(
                new OrbBomberSequenceResetMessage { OrbNetId = identity.netId }
            );
        }

        private void ReleaseToPhysics(bool clearVelocity)
        {
            _rb.constraints = RigidbodyConstraints.None;
            _rb.useGravity = true;
            _rb.isKinematic = false;
            _rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            if (!clearVelocity)
                return;

            _rb.linearVelocity = Vector3.zero;
            _rb.angularVelocity = Vector3.zero;
        }

        private static Vector3 TumbleSpin(Vector3 travelDirection, float force)
        {
            Vector3 axis = Vector3.Cross(Vector3.up, travelDirection);
            if (axis.sqrMagnitude < 0.001f)
                axis = Vector3.right;
            return axis.normalized * (force * SpinPerImpulse);
        }

        public static void ServerHandleSwingMessage(NetworkConnectionToClient conn, uint orbNetId)
        {
            if (!NetworkServer.active || conn?.identity == null)
                return;
            if (!NetworkServer.spawned.TryGetValue(orbNetId, out var identity))
                return;

            var behaviour = identity.GetComponent<OrbBomberBehaviour>();
            var swinger = conn.identity.GetComponent<PlayerInventory>()?.PlayerInfo;
            behaviour?.ServerHandleSwing(swinger);
        }

        private void TickApproach(Vector3 targetPosition)
        {
            // Low gravity scales Physics.gravity, which kinematic bodies ignore.
            // Chase with a dynamic body for that window so bumps and falls hang.
            // The planted kinematic chase stays for normal gravity: a fully
            // dynamic chase would still need this state machine, because writing
            // a homing velocity every tick cancels clubs, blasts, and the black hole.
            if (LowGravityItem.IsActive)
                TickApproachDynamic(targetPosition);
            else
                TickApproachPlanted(targetPosition);
        }

        private void TickApproachPlanted(Vector3 targetPosition)
        {
            if (!_rb.isKinematic)
                EnterApproachBody();

            FaceTarget(targetPosition);

            if (!TryApproachStep(targetPosition, out Vector3 next, out float speed, out Vector3 direction))
                return;

            if (speed > 0f && direction.sqrMagnitude > 0.0001f)
            {
                float distance = new Vector3(
                    targetPosition.x - _rb.position.x,
                    0f,
                    targetPosition.z - _rb.position.z
                ).magnitude;
                float step = Mathf.Min(speed * Time.fixedDeltaTime, distance);
                next += direction * step;
            }

            if (TryFindGround(next, out float groundY))
                next.y = groundY + _setup.BaseRadius;

            _rb.MovePosition(next);
        }

        private void TickApproachDynamic(Vector3 targetPosition)
        {
            _rb.constraints = RigidbodyConstraints.FreezeRotation;
            _rb.useGravity = true;
            if (_rb.isKinematic)
                _rb.isKinematic = false;
            if (_rb.collisionDetectionMode != CollisionDetectionMode.ContinuousDynamic)
                _rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

            KeepAboveGround();

            FaceTarget(targetPosition);

            if (!TryApproachStep(targetPosition, out _, out float speed, out Vector3 direction))
                return;

            Vector3 velocity = _rb.linearVelocity;
            velocity.x = direction.x * speed;
            velocity.z = direction.z * speed;
            _rb.linearVelocity = velocity;
        }

        /// False when the orb starts detonating. A cancelled countdown keeps chasing
        /// through the bubble until the target has been outside DetonationRange.
        private bool TryApproachStep(
            Vector3 targetPosition,
            out Vector3 next,
            out float speed,
            out Vector3 direction
        )
        {
            next = _rb.position;
            Vector3 toTarget = targetPosition - _rb.position;
            toTarget.y = 0f;
            float distance = toTarget.magnitude;
            direction = distance > 0.001f ? toTarget / distance : Vector3.zero;

            float detonationRange = Mathf.Max(0.5f, ModConfig.OrbBomber.DetonationRange.Value);
            if (distance > detonationRange)
                _mustLeaveRange = false;
            else if (!_mustLeaveRange)
            {
                speed = 0f;
                BeginDetonation();
                return false;
            }

            float far = Mathf.Max(
                ModConfig.OrbBomber.FarSpeedDistance.Value,
                detonationRange + 0.01f
            );
            float blend = Mathf.InverseLerp(far, detonationRange, distance);
            speed = Mathf.Max(
                0f,
                Mathf.Lerp(
                    ModConfig.OrbBomber.StartSpeed.Value + _angerBonus,
                    ModConfig.OrbBomber.MaxSpeed.Value + _angerBonus,
                    blend
                )
            );
            return true;
        }

        private void BeginDetonation()
        {
            if (!_rb.isKinematic)
            {
                _rb.linearVelocity = Vector3.zero;
                _rb.angularVelocity = Vector3.zero;
                _rb.constraints = RigidbodyConstraints.None;
                _rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
                _rb.useGravity = false;
                _rb.isKinematic = true;
            }

            _phase = Phase.Detonating;
            _plantedCenterY = _rb.position.y;
            _sequenceStart = Time.time;
            _sequenceDuration = Mathf.Max(0.1f, ModConfig.OrbBomber.DetonationDuration.Value);
            int flashes = Mathf.Max(1, Mathf.RoundToInt(ModConfig.OrbBomber.FlashCount.Value));
            float size = Mathf.Max(1f, ModConfig.OrbBomber.SizeMultiplier.Value);

            _setup.PlaySequence(_sequenceDuration, flashes, size);

            var identity = GetComponent<NetworkIdentity>();
            if (identity != null)
            {
                NetworkServer.SendToAll(
                    new OrbBomberSequenceStartMessage
                    {
                        OrbNetId = identity.netId,
                        Duration = _sequenceDuration,
                        FlashCount = flashes,
                        SizeMultiplier = size,
                    }
                );
            }
        }

        private void TickDetonation(Vector3 targetPosition)
        {
            if (ModConfig.OrbBomber.CancelDetonationOutOfRange.Value)
            {
                Vector3 flat = targetPosition - _rb.position;
                flat.y = 0f;
                float range = Mathf.Max(0.5f, ModConfig.OrbBomber.DetonationRange.Value);
                if (flat.sqrMagnitude > range * range)
                {
                    InterruptDetonationVisual();
                    EnterApproachBody();
                    return;
                }
            }

            FaceTarget(targetPosition);

            float elapsed = Time.time - _sequenceStart;
            float scale = _setup.ApplyVisual(elapsed);
            Vector3 next = _rb.position;
            next.y = _plantedCenterY + (scale - 1f) * _setup.BaseRadius;
            _rb.MovePosition(next);

            if (elapsed >= _sequenceDuration)
                Detonate();
        }

        private void FaceTarget(Vector3 worldPosition)
        {
            Vector3 flat = worldPosition - _rb.position;
            flat.y = 0f;
            if (flat.sqrMagnitude < 0.0001f)
                return;

            _rb.MoveRotation(Quaternion.LookRotation(flat, Vector3.up));
        }

        private void TickFlung()
        {
            KeepAboveGround();

            // A short probe. The chase ray looks several metres down, which would
            // still "find ground" at the top of a fling and skip the landing.
            bool grounded = IsRestingOnGround();
            if (!grounded)
                _leftGround = true;

            if (Time.time < _ignoreUntil)
                return;

            float keep = 1f / (1f + FlungDrag * Time.fixedDeltaTime);
            _rb.linearVelocity *= keep;
            _rb.angularVelocity *= keep;

            bool touchdown = false;
            if (_leftGround && grounded)
            {
                touchdown = true;
                _leftGround = false;
            }

            bool fastContact = _pendingFastContact;
            _pendingFastContact = false;
            bool realLanding = fastContact || touchdown;
            float speed = _rb.linearVelocity.magnitude;

            if (_impactArmed && realLanding && speed > ResumeSpeed())
            {
                Detonate();
                return;
            }

            // Stay dynamic until the bottom is actually near the ground. Taking
            // over at the top of a lob would either hover there or snap down.
            if (speed <= ResumeSpeed() && grounded)
                EnterApproachBody();
        }

        private static float ResumeSpeed() =>
            Mathf.Max(0.05f, ModConfig.OrbBomber.SettleSpeed.Value);

        /// If the pivot has gone through the nearest ground, put the bottom back on top.
        private void KeepAboveGround()
        {
            if (_rb == null || _setup == null || _rb.isKinematic)
                return;

            float radius = _setup.BaseRadius * Mathf.Max(transform.localScale.y, 1f);
            Vector3 origin = _rb.position + Vector3.up * 12f;
            int count = Physics.RaycastNonAlloc(
                origin,
                Vector3.down,
                GroundHits,
                24f,
                GroundMask,
                QueryTriggerInteraction.Ignore
            );

            float best = float.MaxValue;
            float surfaceY = 0f;
            bool found = false;
            for (int i = 0; i < count; i++)
            {
                var hit = GroundHits[i];
                if (!IsUsableGround(hit))
                    continue;

                float gap = Mathf.Abs(hit.point.y - _rb.position.y);
                if (gap >= best)
                    continue;
                best = gap;
                surfaceY = hit.point.y;
                found = true;
            }

            float minY = surfaceY + radius;
            if (!found || best > 8f || _rb.position.y >= minY - 0.05f)
                return;

            var position = _rb.position;
            position.y = minY;
            _rb.position = position;
            if (_rb.linearVelocity.y < 0f)
            {
                var velocity = _rb.linearVelocity;
                velocity.y = 0f;
                _rb.linearVelocity = velocity;
            }
        }

        private void EnterApproachBody()
        {
            _phase = Phase.Approach;
            _impactArmed = false;
            _leftGround = false;
            _pendingFastContact = false;
            _reapplyKnock = false;
            if (!_rb.isKinematic)
            {
                _rb.linearVelocity = Vector3.zero;
                _rb.angularVelocity = Vector3.zero;
            }

            _rb.constraints = RigidbodyConstraints.None;
            _rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            _rb.useGravity = false;
            _rb.isKinematic = true;
        }

        private int GroundMask =>
            _groundMask == 0 ? Physics.DefaultRaycastLayers : _groundMask;

        private bool TryFindGround(Vector3 position, out float groundY)
        {
            groundY = position.y;
            float lift = Mathf.Max(_setup.BodyRadius, Mathf.Max(0f, _setup.BaseRadius)) + 3f;
            float castDistance = lift + 80f;
            Vector3 origin = position + Vector3.up * lift;
            int count = Physics.RaycastNonAlloc(
                origin,
                Vector3.down,
                GroundHits,
                castDistance,
                GroundMask,
                QueryTriggerInteraction.Ignore
            );

            float bestDistance = float.MaxValue;
            bool found = false;
            for (int i = 0; i < count; i++)
            {
                var hit = GroundHits[i];
                if (!IsUsableGround(hit))
                    continue;
                if (hit.distance < bestDistance)
                {
                    bestDistance = hit.distance;
                    groundY = hit.point.y;
                    found = true;
                }
            }

            // A full buffer drops hits in undefined order, so the ground under the
            // orb can be missing. The single raycast is the closest surface.
            if (
                count >= GroundHits.Length
                && Physics.Raycast(
                    origin,
                    Vector3.down,
                    out RaycastHit closest,
                    castDistance,
                    GroundMask,
                    QueryTriggerInteraction.Ignore
                )
                && IsUsableGround(closest)
                && (!found || closest.distance < bestDistance)
            )
            {
                groundY = closest.point.y;
                found = true;
            }

            return found;
        }

        private bool IsUsableGround(RaycastHit hit)
        {
            if (hit.collider == null || hit.normal.y < 0.45f)
                return false;
            return hit.collider.transform != transform && !hit.collider.transform.IsChildOf(transform);
        }

        private bool IsRestingOnGround()
        {
            float reach = Mathf.Max(0f, _setup.BaseRadius) * Mathf.Max(transform.localScale.y, 1f) + 0.5f;
            int count = Physics.RaycastNonAlloc(
                _rb.position,
                Vector3.down,
                GroundHits,
                reach,
                GroundMask,
                QueryTriggerInteraction.Ignore
            );

            for (int i = 0; i < count; i++)
            {
                if (IsUsableGround(GroundHits[i]))
                    return true;
            }

            return false;
        }

        /// The player, or their golf ball when <see cref="OrbBomberConfig.ChaseGolfBall"/> is on.
        internal static Transform ChaseTransform(PlayerInfo target)
        {
            if (target == null)
                return null;

            if (!ModConfig.OrbBomber.ChaseGolfBall.Value)
                return target.transform;

            var ball = target.AsGolfer?.OwnBall;
            return ball != null ? ball.transform : null;
        }

        private void Detonate()
        {
            if (_finished)
                return;
            _finished = true;

            Vector3 pos = _rb != null ? _rb.worldCenterOfMass : transform.position;
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
                ExplosionScaler.Register(tempRocket, ModConfig.OrbBomber.ExplosionScale.Value);
                if (ServerExplodeMethod == null)
                {
                    IssaPluginPlugin.Log.LogError(
                        "[OrbBomber] Rocket.ServerExplode was not found. The orb was removed without a blast."
                    );
                }
                else
                {
                    ServerExplodeMethod.Invoke(tempRocket, new object[] { pos });
                }
            }

            NetworkServer.Destroy(gameObject);
        }

        private void Despawn()
        {
            if (_finished)
                return;
            _finished = true;
            NetworkServer.Destroy(gameObject);
        }

        public static void ServerCleanupAll()
        {
            if (!NetworkServer.active)
                return;

            var orbs = Object.FindObjectsByType<OrbBomberBehaviour>(FindObjectsSortMode.None);
            foreach (var orb in orbs)
                orb.Despawn();
        }
    }
}
