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
            if (!_isIgnoringThrower)
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
            if (_throwerTransform == null)
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
            // Knockouts / impulses are server-authoritative; remote clients only
            // receive the NetworkTransform visual and never run this component.
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

            // ── Player knockout ───────────────────────────────────────────────
            // Cart / prop motion comes only from Unity's contact solver (ball mass ×
            // velocity). No extra AddForce — that was launching carts.
            var movement = collision.gameObject.GetComponentInParent<PlayerMovement>();
            if (movement == null)
                return;

            if (movement.GetComponent<NetworkIdentity>() == null)
                return;

            if (ThrowerInfo == null)
                return;

            ContactPoint contact = collision.GetContact(0);
            Vector3 hitPosition = contact.point;

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
