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

        private const float UpwardBias = 0.5f;
        private const float SpinPerImpulse = 0.55f;
        private const float ExplosionForce = 15f;
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
        private int _groundMask;

        private static readonly RaycastHit[] GroundHits = new RaycastHit[8];
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
                return;

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
                Mathf.Abs(_setup.BaseRadius) * Mathf.Max(transform.localScale.x, 1f) + 0.75f;
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
                if (!_rb.isKinematic && cartSimulated)
                    return;

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

            InterruptDetonationVisual();
            ReleaseToPhysics(clearVelocity: true);

            Vector3 direction = cartVelocity.normalized;
            float speed = cartVelocity.magnitude * CartKnockScale;
            _rb.linearVelocity = direction * speed + Vector3.up * Mathf.Clamp(speed * 0.15f, 0.5f, 3f);
            ApplyTumble(direction, speed);

            _phase = Phase.Flung;
            _ignoreUntil = Time.time + ImpactIgnoreSeconds;
            _leftGround = false;
            _pendingFastContact = false;
            _cartKnockCooldown = Time.time + 0.45f;
            return true;
        }

        public void ServerHandleSwing(PlayerInfo swinger)
        {
            if (!NetworkServer.active || _finished || swinger == null || _setup == null || _rb == null)
                return;

            var swingerIdentity = swinger.GetComponent<NetworkIdentity>();
            if (swingerIdentity == null)
                return;

            float bodyRadius = Mathf.Abs(_setup.BaseRadius) * Mathf.Max(transform.localScale.x, 1f);
            float reach = SwingOverlapRadius + bodyRadius + 2f;
            if ((swinger.transform.position - transform.position).sqrMagnitude > reach * reach)
                return;

            bool isVictim = swingerIdentity.netId == VictimNetId;
            InterruptDetonationVisual();

            Vector3 away = transform.position - swinger.transform.position;
            if (away.sqrMagnitude < 0.0001f)
                away = swinger.transform.forward;
            else
                away.Normalize();

            Vector3 knockDir = (away + Vector3.up * UpwardBias).normalized;
            float force =
                Mathf.Max(0f, ModConfig.OrbBomber.ClubKnockbackForce.Value)
                * SwingForceMultiplier(swinger);

            // Become dynamic before writing velocity. Unity ignores velocity
            // assigned to a kinematic body, so the impulse would be the only
            // change and a second hit could not replace the previous fling.
            ReleaseToPhysics(clearVelocity: true);
            _rb.AddForce(knockDir * force, ForceMode.Impulse);
            ApplyTumble(knockDir, force);

            _phase = Phase.Flung;
            _ignoreUntil = Time.time + ImpactIgnoreSeconds;
            _leftGround = false;
            _pendingFastContact = false;
            if (isVictim)
            {
                _impactArmed =
                    Random.value < Mathf.Clamp01(ModConfig.OrbBomber.ImpactExplodeChance.Value);
            }
        }

        /// Black hole suction already called AddForce. Stay dynamic and let that force stick.
        public void NotifyBlackHoleSuction()
        {
            if (_finished || _rb == null)
                return;

            _blackHoleSuppressedUntil = Time.fixedTime + Time.fixedDeltaTime * 2f;
            InterruptDetonationVisual();
            ReleaseToPhysics(clearVelocity: false);
            _phase = Phase.Flung;
        }

        /// Called just before the black hole writes the spit velocity.
        public void NotifyBlackHoleSpitLaunch()
        {
            if (_finished || _rb == null)
                return;

            _blackHoleSuppressedUntil = Time.fixedTime + 2f;
            InterruptDetonationVisual();
            ReleaseToPhysics(clearVelocity: false);
            _phase = Phase.Flung;
        }

        public void ApplyExplosion(Vector3 origin, float radius, float scale)
        {
            if (_finished || _rb == null || _setup == null)
                return;

            InterruptDetonationVisual();
            ReleaseToPhysics(clearVelocity: false);
            _phase = Phase.Flung;
            _ignoreUntil = Time.time + ImpactIgnoreSeconds;
            _leftGround = false;
            _pendingFastContact = false;

            float scaledRadius = Mathf.Max(0.5f, radius);
            _rb.AddExplosionForce(
                ExplosionForce * Mathf.Max(1f, scale),
                origin,
                scaledRadius,
                0.5f,
                ForceMode.VelocityChange
            );

            Vector3 away = transform.position - origin;
            if (away.sqrMagnitude < 0.0001f)
                away = Vector3.up;
            ApplyTumble(away.normalized, ExplosionForce * Mathf.Max(1f, scale));
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
            bool restorePlantedHeight = _phase == Phase.Detonating;
            _setup.ResetSequence();
            if (!restorePlantedHeight)
                return;

            Vector3 planted = _rb.position;
            planted.y = _plantedCenterY;
            _rb.position = planted;

            var identity = GetComponent<NetworkIdentity>();
            if (identity != null)
            {
                NetworkServer.SendToAll(
                    new OrbBomberSequenceResetMessage { OrbNetId = identity.netId }
                );
            }
        }

        private void ReleaseToPhysics(bool clearVelocity)
        {
            _rb.useGravity = true;
            _rb.isKinematic = false;
            if (!clearVelocity)
                return;

            _rb.linearVelocity = Vector3.zero;
            _rb.angularVelocity = Vector3.zero;
        }

        private void ApplyTumble(Vector3 travelDirection, float force)
        {
            Vector3 axis = Vector3.Cross(Vector3.up, travelDirection);
            if (axis.sqrMagnitude < 0.001f)
                axis = Vector3.right;
            _rb.angularVelocity = axis.normalized * (force * SpinPerImpulse);
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
            FaceTarget(targetPosition);

            Vector3 toTarget = targetPosition - _rb.position;
            toTarget.y = 0f;
            float distance = toTarget.magnitude;

            float detonationRange = Mathf.Max(0.5f, ModConfig.OrbBomber.DetonationRange.Value);
            if (distance <= detonationRange)
            {
                BeginDetonation();
                return;
            }

            float far = Mathf.Max(
                ModConfig.OrbBomber.FarSpeedDistance.Value,
                detonationRange + 0.01f
            );
            float blend = Mathf.InverseLerp(far, detonationRange, distance);
            float speed = Mathf.Max(
                0f,
                Mathf.Lerp(
                    ModConfig.OrbBomber.StartSpeed.Value,
                    ModConfig.OrbBomber.MaxSpeed.Value,
                    blend
                )
            );

            Vector3 next = _rb.position;
            if (distance > 0.001f)
            {
                float step = Mathf.Min(speed * Time.fixedDeltaTime, distance);
                next += toTarget / distance * step;
            }

            if (TryFindGround(next, out float groundY))
                next.y = groundY + _setup.BaseRadius;

            _rb.MovePosition(next);
        }

        private void BeginDetonation()
        {
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
            // A short probe. The chase ray looks several metres down, which would
            // still "find ground" at the top of a fling and skip the landing.
            bool grounded = IsRestingOnGround();
            if (!grounded)
                _leftGround = true;

            if (Time.time < _ignoreUntil)
                return;

            bool touchdown = false;
            if (_leftGround && grounded)
            {
                touchdown = true;
                _leftGround = false;
            }

            bool fastContact = _pendingFastContact;
            _pendingFastContact = false;
            bool realLanding = fastContact || touchdown;

            if (_impactArmed && realLanding)
            {
                Detonate();
                return;
            }

            float settle = Mathf.Max(0.05f, ModConfig.OrbBomber.SettleSpeed.Value);
            if (grounded && _rb.linearVelocity.magnitude <= settle && !realLanding)
                EnterApproachBody();
        }

        private void EnterApproachBody()
        {
            _phase = Phase.Approach;
            _impactArmed = false;
            _leftGround = false;
            _pendingFastContact = false;
            if (!_rb.isKinematic)
            {
                _rb.linearVelocity = Vector3.zero;
                _rb.angularVelocity = Vector3.zero;
            }

            _rb.useGravity = false;
            _rb.isKinematic = true;
        }

        private bool TryFindGround(Vector3 position, out float groundY)
        {
            groundY = position.y;
            Vector3 origin = position + Vector3.up * (_setup.BaseRadius + 3f);
            int count = Physics.RaycastNonAlloc(
                origin,
                Vector3.down,
                GroundHits,
                _setup.BaseRadius + 8f,
                _groundMask == 0 ? Physics.DefaultRaycastLayers : _groundMask,
                QueryTriggerInteraction.Ignore
            );

            float bestDistance = float.MaxValue;
            bool found = false;
            for (int i = 0; i < count; i++)
            {
                var hit = GroundHits[i];
                if (hit.collider == null)
                    continue;
                if (hit.collider.transform.IsChildOf(transform) || hit.collider.transform == transform)
                    continue;
                if (hit.distance < bestDistance)
                {
                    bestDistance = hit.distance;
                    groundY = hit.point.y;
                    found = true;
                }
            }

            return found;
        }

        private bool IsRestingOnGround()
        {
            float reach = _setup.BaseRadius * Mathf.Max(transform.localScale.y, 1f) + 0.5f;
            int count = Physics.RaycastNonAlloc(
                _rb.position,
                Vector3.down,
                GroundHits,
                reach,
                _groundMask == 0 ? Physics.DefaultRaycastLayers : _groundMask,
                QueryTriggerInteraction.Ignore
            );

            for (int i = 0; i < count; i++)
            {
                var hit = GroundHits[i];
                if (hit.collider == null)
                    continue;
                if (hit.collider.transform == transform || hit.collider.transform.IsChildOf(transform))
                    continue;
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

            Vector3 pos = transform.position;
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
                ServerExplodeMethod?.Invoke(tempRocket, new object[] { pos });
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
