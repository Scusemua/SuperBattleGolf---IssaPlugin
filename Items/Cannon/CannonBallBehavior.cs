using Mirror;
using UnityEngine;

namespace IssaPlugin.Items.Cannon
{
    public class CannonBallBehavior : CustomHittable
    {
        /// <summary>PlayerInfo of the player who fired this ball (for kill attribution).</summary>
        public PlayerInfo ThrowerInfo;

        public Vector3 InitialVelocity;

        public float ThrowerIgnoreDuration = 2.0f;

        // Distance from the thrower beyond which the grace period is ended early. Once the
        // ball is this far clear, keeping the exemption alive would only let it pass
        // harmlessly through its owner, so we drop it as soon as it is no longer needed.
        private const float ThrowerClearDistance = 4f;

        private const float MinKnockoutSpeed = 5f;

        // Countdown for the thrower-collision grace period, and the thrower's transform
        // cached at Start so the per-frame check is a cheap IsChildOf rather than a
        // GetComponentInParent walk.
        private float _throwerIgnoreTimer;
        private Transform _throwerTransform;

        private Rigidbody _rb;

        // Collider pairs we IgnoreCollision'd at spawn so we can restore them when the
        // grace period ends (otherwise the shooter stays permanently intangible).
        private Collider[] _ballColliders;
        private Collider[] _ignoredThrowerColliders;
        private bool _isIgnoringThrower;

        private void Start()
        {
            _throwerIgnoreTimer = ThrowerIgnoreDuration;
            _throwerTransform = ThrowerInfo != null ? ThrowerInfo.transform : null;

            _rb = GetComponent<Rigidbody>();
            if (_rb == null)
                _rb = gameObject.AddComponent<Rigidbody>();

            _rb.useGravity = true;
            _rb.isKinematic = false;
            _rb.linearVelocity = InitialVelocity;
            _rb.mass =
                ModConfig.Cannon.BowlingBallMass.Value
                * ModConfig.Cannon.BowlingBallMassMultiplier.Value;

            // Bridge usually calls this before the first physics step; Start is a
            // fallback if the ball was spawned some other way.
            if (!_isIgnoringThrower && ThrowerIgnoreDuration > 0f)
                BeginIgnoreThrower();
        }

        /// <summary>
        /// Ignores every collider on the thrower until the ball clears them.
        /// Safe to call more than once; subsequent calls are no-ops while active.
        /// </summary>
        public void BeginIgnoreThrower()
        {
            if (_isIgnoringThrower)
                return;

            _throwerIgnoreTimer = ThrowerIgnoreDuration;
            _throwerTransform = ThrowerInfo != null ? ThrowerInfo.transform : null;
            if (_throwerTransform == null || ThrowerIgnoreDuration <= 0f)
                return;

            _ballColliders = GetComponentsInChildren<Collider>(true);
            _ignoredThrowerColliders = _throwerTransform.GetComponentsInChildren<Collider>(true);

            for (int i = 0; i < _ballColliders.Length; i++)
            {
                var ballCol = _ballColliders[i];
                if (ballCol == null)
                    continue;

                for (int j = 0; j < _ignoredThrowerColliders.Length; j++)
                {
                    var throwerCol = _ignoredThrowerColliders[j];
                    if (throwerCol == null)
                        continue;

                    Physics.IgnoreCollision(ballCol, throwerCol, true);
                }
            }

            _isIgnoringThrower = true;
        }

        private void EndIgnoreThrower()
        {
            if (!_isIgnoringThrower)
                return;

            if (_ballColliders != null && _ignoredThrowerColliders != null)
            {
                for (int i = 0; i < _ballColliders.Length; i++)
                {
                    var ballCol = _ballColliders[i];
                    if (ballCol == null)
                        continue;

                    for (int j = 0; j < _ignoredThrowerColliders.Length; j++)
                    {
                        var throwerCol = _ignoredThrowerColliders[j];
                        if (throwerCol == null)
                            continue;

                        Physics.IgnoreCollision(ballCol, throwerCol, false);
                    }
                }
            }

            _isIgnoringThrower = false;
            _throwerIgnoreTimer = 0f;
            _ballColliders = null;
            _ignoredThrowerColliders = null;
        }

        private void OnDestroy()
        {
            // Restore thrower collisions if the ball despawns mid-grace so we don't
            // leave a permanent IgnoreCollision pair against a destroyed collider.
            EndIgnoreThrower();
        }

        void OnCollisionEnter(Collision collision)
        {
            // Knockouts / hittable reactions are server-authoritative; remote clients
            // only receive the NetworkTransform visual and never run this component.
            if (!NetworkServer.active)
                return;

            if (
                _isIgnoringThrower
                && _throwerTransform != null
                && collision.gameObject.transform.IsChildOf(_throwerTransform)
            )
                return;

            if (_rb == null || _rb.linearVelocity.magnitude <= MinKnockoutSpeed)
                return;

            if (ThrowerInfo == null)
                return;

            // Contact modification (and some PhysicsManager foliage/terrain paths) can
            // deliver OnCollisionEnter with contactCount == 0. GetContact(0) throws then.
            Vector3 hitPosition;
            if (collision.contactCount > 0)
                hitPosition = collision.GetContact(0).point;
            else
                hitPosition = collision.collider != null
                    ? collision.collider.ClosestPoint(_rb.position)
                    : transform.position;

            Vector3 hitDirection = _rb.linearVelocity.normalized;
            if (hitDirection.sqrMagnitude < 0.0001f)
                hitDirection = (hitPosition - transform.position).normalized;

            // ── Player knockout ───────────────────────────────────────────────
            // Cart / prop motion comes only from Unity's contact solver (ball mass ×
            // velocity). No extra AddForce — that was launching carts.
            var movement = collision.gameObject.GetComponentInParent<PlayerMovement>();
            if (movement != null)
            {
                if (movement.GetComponent<NetworkIdentity>() == null)
                    return;

                float dist = Vector3.Distance(ThrowerInfo.transform.position, hitPosition);

                movement.TryKnockOut(
                    ThrowerInfo,
                    KnockoutType.Rocket,
                    false,
                    movement.transform.InverseTransformPoint(hitPosition),
                    dist,
                    _rb.linearVelocity,
                    ElectromagnetShieldHitBlockType.FullyBlocked,
                    new ItemUseId(
                        ThrowerInfo.PlayerId.Guid,
                        BlackHoleGrenadeItem.NextUseIndex(),
                        ItemType.RocketLauncher,
                        false
                    ),
                    false,
                    true,
                    out _,
                    out _
                );
                return;
            }

            // ── Target dummies / other non-player Hittables ───────────────────
            // Guns call Hittable.HitWithItem, which raises WasHitByItem — the event
            // TargetDummy listens to for its flinch/spin animations. Without this,
            // a physics collision alone never triggers that reaction.
            var hittable = collision.gameObject.GetComponentInParent<Hittable>();
            if (hittable == null)
                return;

            // Players are handled above via TryKnockOut; HitWithItem on a player would
            // stack a second gun-style hit response on top of the knockout.
            if (hittable.AsEntity != null && hittable.AsEntity.IsPlayer)
                return;

            // Golf carts already move from the Rigidbody contact solver. HitWithItem
            // with RocketLauncher would apply a second rocket-style impulse on top.
            if (hittable.GetComponentInParent<GolfCartInfo>() != null)
                return;

            var inventory = ThrowerInfo.GetComponent<PlayerInventory>();
            if (inventory == null)
                return;

            Vector3 localHitPoint = hittable.transform.InverseTransformPoint(hitPosition);
            Vector3 localOrigin = hittable.transform.InverseTransformPoint(
                ThrowerInfo.transform.position
            );
            float distance = Vector3.Distance(ThrowerInfo.transform.position, hitPosition);

            var useId = new ItemUseId(
                ThrowerInfo.PlayerId.Guid,
                BlackHoleGrenadeItem.NextUseIndex(),
                ItemType.RocketLauncher,
                false
            );

            hittable.HitWithItem(
                ItemType.RocketLauncher,
                useId,
                localHitPoint,
                hitDirection,
                localOrigin,
                distance,
                inventory,
                false,
                false,
                false,
                NetworkTime.time,
                0UL
            );
        }

        public void FixedUpdate()
        {
            if (!_isIgnoringThrower && _throwerIgnoreTimer <= 0f)
                return;

            if (_throwerIgnoreTimer > 0f)
                _throwerIgnoreTimer -= Time.fixedDeltaTime;

            bool clearOfThrower = false;
            if (_throwerTransform == null)
            {
                clearOfThrower = true;
            }
            else
            {
                float sqToThrower = (_throwerTransform.position - transform.position).sqrMagnitude;
                clearOfThrower = sqToThrower > ThrowerClearDistance * ThrowerClearDistance;
            }

            if (_throwerIgnoreTimer <= 0f || clearOfThrower)
                EndIgnoreThrower();
        }
    }
}
