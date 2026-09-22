using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using Mirror;
using UnityEngine;

namespace IssaPlugin.Items.Cannon
{
    public class BowlingBallBehavior : CustomHittable
    {
        /// <summary>PlayerInfo of the player who fired this ball (for kill attribution).</summary>
        public PlayerInfo ThrowerInfo;

        public Vector3 InitialVelocity;
        public ItemUseId ImpactItemUseId;

        public float ThrowerIgnoreDuration = 2.0f;

        // Distance from the thrower beyond which the grace period is ended early. Once the
        // ball is this far clear, keeping the exemption alive would only let it pass
        // harmlessly through its owner, so we drop it as soon as it is no longer needed.
        private const float ThrowerClearDistance = 4f;

        private const float MinHittableImpactSpeed = 5f;
        // A player can have several colliders. Suppress duplicate OnCollisionEnter
        // callbacks for the same window the base game ignores repeat cart contacts.
        private readonly Dictionary<uint, double> _nextAllowedPlayerHitTime = new();

        // Countdown for the thrower-collision grace period, and the thrower's transform
        // cached at Start so the per-frame check is a cheap IsChildOf rather than a
        // GetComponentInParent walk.
        private float _throwerIgnoreTimer;
        private Transform _throwerTransform;

        private Rigidbody _rb;
        private Vector3 _velocityBeforePhysics;

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
            _velocityBeforePhysics = _rb.linearVelocity;
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

            if (_rb == null)
                return;

            // Contact modification (and some PhysicsManager foliage/terrain paths) can
            // deliver OnCollisionEnter with contactCount == 0. GetContact(0) throws then.
            Vector3 hitPosition;
            if (collision.contactCount > 0)
                hitPosition = collision.GetContact(0).point;
            else
                hitPosition =
                    collision.collider != null
                        ? collision.collider.ClosestPoint(_rb.position)
                        : transform.position;

            // FixedUpdate cached the ball's absolute velocity before this physics step.
            // Rigidbody.linearVelocity is already post-collision inside OnCollisionEnter.
            Vector3 incidentVelocity = _velocityBeforePhysics;
            Vector3 impactDirectionVelocity =
                incidentVelocity.sqrMagnitude >= 0.0001f
                    ? incidentVelocity
                    : -collision.relativeVelocity;

            Vector3 hitDirection = impactDirectionVelocity.normalized;
            if (hitDirection.sqrMagnitude < 0.0001f)
                hitDirection = (hitPosition - transform.position).normalized;

            // ── Player knockout ───────────────────────────────────────────────
            // Cart / prop motion comes only from Unity's contact solver.
            //
            // Knockout is applied on the VICTIM's client (Nuke / Flamethrower pattern).
            // TryKnockOut ends in a Command that requires an active owning client;
            // calling it on the server copy fails on dedicated servers and is the
            // wrong authority model even on a listen host for remote victims.
            var movement = collision.gameObject.GetComponentInParent<PlayerMovement>();
            if (movement != null)
            {
                var victimIdentity = movement.GetComponent<NetworkIdentity>();
                if (victimIdentity == null)
                    return;

                // Occupied carts receive the physical ball collision themselves. Do not
                // detach and launch a seated passenger independently from their vehicle.
                if (
                    movement.PlayerInfo != null
                    && movement.PlayerInfo.ActiveGolfCartSeat.IsValid()
                )
                    return;

                var cartSettings = GameManager.GolfCartSettings;
                float incidentSpeedSq = incidentVelocity.sqrMagnitude;
                float relativeSpeedSq = collision.relativeVelocity.sqrMagnitude;
                if (
                    incidentSpeedSq < cartSettings.RunOverPlayerKnockoutMinSpeedSquared
                    || relativeSpeedSq
                        < cartSettings.RunOverPlayerKnockoutMinRelativeSpeedSquared
                )
                    return;

                double now = NetworkTime.time;
                if (
                    _nextAllowedPlayerHitTime.TryGetValue(
                        victimIdentity.netId,
                        out double nextAllowedHit
                    )
                    && now < nextAllowedHit
                )
                    return;
                double repeatSuppressionDuration =
                    movement.PlayerInfo != null
                    && movement.PlayerInfo.IsInJumboBurgerGiantForm
                        ? 0.2
                        : 0.5;
                _nextAllowedPlayerHitTime[victimIdentity.netId] =
                    now + repeatSuppressionDuration;
                StartCoroutine(
                    TemporarilyIgnoreVictimCollisions(
                        movement.transform,
                        (float)repeatSuppressionDuration
                    )
                );

                var throwerIdentity = ThrowerInfo?.GetComponent<NetworkIdentity>();
                uint throwerNetId = throwerIdentity != null ? throwerIdentity.netId : 0u;

                float relativeSpeed = Mathf.Sqrt(relativeSpeedSq);
                float impactT = Mathf.InverseLerp(
                    cartSettings.RunOverPlayerKnockoutMinRelativeSpeed,
                    cartSettings.RunOverPlayerKnockoutMaxRelativeSpeed,
                    relativeSpeed
                );
                float horizontalKnockback = Mathf.Lerp(
                    cartSettings.RunOverPlayerKnockoutMinHorizontalKnockback,
                    cartSettings.RunOverPlayerKnockoutMaxHorizontalKnockback,
                    impactT
                );
                float verticalKnockback = Mathf.Lerp(
                    cartSettings.RunOverPlayerKnockoutMinVerticalKnockback,
                    cartSettings.RunOverPlayerKnockoutMaxVerticalKnockback,
                    impactT
                );

                Vector3 horizontalDirection = Vector3.zero;
                if (collision.contactCount > 0)
                {
                    horizontalDirection = -collision.GetContact(0).normal;
                    horizontalDirection.y = 0f;
                    horizontalDirection.Normalize();
                }

                Vector3 incidentHorizontal = impactDirectionVelocity;
                incidentHorizontal.y = 0f;
                incidentHorizontal.Normalize();
                if (horizontalDirection.sqrMagnitude < 0.0001f)
                    horizontalDirection = incidentHorizontal;
                else if (
                    incidentHorizontal.sqrMagnitude > 0.0001f
                    && Vector3.Dot(horizontalDirection, incidentHorizontal) < 0f
                )
                    horizontalDirection = -horizontalDirection;

                float knockbackMultiplier = ModConfig.Cannon.PlayerKnockbackMultiplier.Value;
                Vector3 playerVelocityChange =
                    (
                        horizontalDirection * horizontalKnockback
                        + Vector3.up * verticalKnockback
                    ) * knockbackMultiplier;

                float dist =
                    ThrowerInfo != null
                        ? Vector3.Distance(ThrowerInfo.transform.position, hitPosition)
                        : 0f;
                var knockoutMsg = new BowlingBallKnockoutMessage
                {
                    VictimNetId = victimIdentity.netId,
                    ThrowerNetId = throwerNetId,
                    LocalHitPoint = movement.transform.InverseTransformPoint(hitPosition),
                    Distance = dist,
                    KnockbackVelocityChange = playerVelocityChange,
                    ItemUseId = ImpactItemUseId,
                };

                // Player movement is owner-authoritative, so only the victim may apply
                // the knockout and velocity. Never broadcast this message.
                if (victimIdentity.connectionToClient != null)
                    victimIdentity.connectionToClient.Send(knockoutMsg);
                else if (
                    NetworkServer.localConnection != null
                    && NetworkServer.localConnection.identity == victimIdentity
                    && NetworkClient.active
                )
                    CannonNetworkBridge.HandleBowlingBallKnockout(knockoutMsg);
                else
                    IssaPluginPlugin.Log.LogWarning(
                        $"[Cannon] No owning connection for victim netId={victimIdentity.netId}; "
                            + "knockout was not delivered."
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

            if (incidentVelocity.magnitude <= MinHittableImpactSpeed)
                return;

            // Players are handled above; HitWithItem on a player would stack a second
            // gun-style hit response on top of the knockout.
            if (hittable.AsEntity != null && hittable.AsEntity.IsPlayer)
                return;

            // Golf carts already move from the Rigidbody contact solver. HitWithItem
            // with RocketLauncher would apply a second rocket-style impulse on top.
            if (hittable.GetComponentInParent<GolfCartInfo>() != null)
                return;

            if (ThrowerInfo == null)
                return;

            var inventory = ThrowerInfo.GetComponent<PlayerInventory>();
            if (inventory == null)
                return;

            Vector3 localHitPoint = hittable.transform.InverseTransformPoint(hitPosition);
            Vector3 localOrigin = hittable.transform.InverseTransformPoint(
                ThrowerInfo.transform.position
            );
            float distance = Vector3.Distance(ThrowerInfo.transform.position, hitPosition);

            // HitWithItem runs HitWithItemInternal locally, then tries CmdHitWithItem
            // to Rpc remotes. On a listen host that Cmd shortcut works. On a dedicated
            // server SendCommandInternal is a no-op (no active client), so we manually
            // broadcast the same Rpc path afterward.
            hittable.HitWithItem(
                ItemType.RocketLauncher,
                ImpactItemUseId,
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

            if (NetworkServer.active && !NetworkClient.active)
                BroadcastItemHitToClients(
                    hittable,
                    ItemType.RocketLauncher,
                    ImpactItemUseId,
                    localHitPoint,
                    hitDirection,
                    localOrigin,
                    distance,
                    inventory,
                    NetworkTime.time
                );
        }

        private IEnumerator TemporarilyIgnoreVictimCollisions(
            Transform victimTransform,
            float duration
        )
        {
            var ballColliders = GetComponentsInChildren<Collider>(true);
            var victimColliders =
                victimTransform != null
                    ? victimTransform.GetComponentsInChildren<Collider>(true)
                    : null;
            if (victimColliders == null)
                yield break;

            SetIgnored(true);
            yield return new WaitForSeconds(duration);
            SetIgnored(false);

            void SetIgnored(bool ignored)
            {
                for (int i = 0; i < ballColliders.Length; i++)
                {
                    var ballCollider = ballColliders[i];
                    if (ballCollider == null)
                        continue;

                    for (int j = 0; j < victimColliders.Length; j++)
                    {
                        var victimCollider = victimColliders[j];
                        if (victimCollider != null)
                            Physics.IgnoreCollision(ballCollider, victimCollider, ignored);
                    }
                }
            }
        }

        /// <summary>
        /// Dedicated-server follow-up for <see cref="Hittable.HitWithItem"/>: invoke the
        /// Command UserCode that TargetRpcs HitWithItemInternal to every connection.
        /// Listen hosts already get that from HitWithItem's Cmd shortcut.
        /// </summary>
        private static void BroadcastItemHitToClients(
            Hittable hittable,
            ItemType itemType,
            ItemUseId itemUseId,
            Vector3 hitLocalPosition,
            Vector3 direction,
            Vector3 localOrigin,
            float distance,
            PlayerInventory itemUser,
            double hitTimestamp
        )
        {
            var method = AccessTools.Method(
                typeof(Hittable),
                "UserCode_CmdHitWithItem__ItemType__ItemUseId__Vector3__Vector3__Vector3__Single__PlayerInventory__Boolean__Boolean__Boolean__Double__UInt64__NetworkConnectionToClient"
            );
            if (method == null)
            {
                IssaPluginPlugin.Log.LogWarning(
                    "[Cannon] Could not find Hittable.UserCode_CmdHitWithItem; "
                        + "dedicated-server TargetDummy hits will not replicate."
                );
                return;
            }

            // sender=null → UserCode skips a second HitWithItemInternal (already ran)
            // and Rpcs every remote connection.
            method.Invoke(
                hittable,
                new object[]
                {
                    itemType,
                    itemUseId,
                    hitLocalPosition,
                    direction,
                    localOrigin,
                    distance,
                    itemUser,
                    false,
                    false,
                    false,
                    hitTimestamp,
                    0UL,
                    null,
                }
            );
        }

        public void FixedUpdate()
        {
            if (_rb != null)
                _velocityBeforePhysics = _rb.linearVelocity;

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
