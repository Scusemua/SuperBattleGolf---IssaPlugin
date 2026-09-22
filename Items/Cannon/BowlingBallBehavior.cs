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
                hitPosition =
                    collision.collider != null
                        ? collision.collider.ClosestPoint(_rb.position)
                        : transform.position;

            Vector3 hitDirection = _rb.linearVelocity.normalized;
            if (hitDirection.sqrMagnitude < 0.0001f)
                hitDirection = (hitPosition - transform.position).normalized;

            // ── Player knockout ───────────────────────────────────────────────
            // Cart / prop motion comes only from Unity's contact solver.
            //
            // Knockout is applied on the VICTIM's client (Nuke / Flamethrower pattern).
            // TryKnockOut ends in a Command that requires an active owning client;
            // calling it on the server copy fails on dedicated servers and is the
            // wrong authority model even on a listen host for remote victims.
            //
            // Shove strength uses a scripted VelocityChange (fraction of ball speed),
            // not collision.impulse — the solver impulse is often enormous and does
            // not match what PlayerMovement keeps after damping. Listen-host victims
            // get the PhysX shove undone here so host and remote share one path.
            var movement = collision.gameObject.GetComponentInParent<PlayerMovement>();
            if (movement != null)
            {
                var victimIdentity = movement.GetComponent<NetworkIdentity>();
                if (victimIdentity == null)
                    return;

                var victimRb = collision.rigidbody;
                if (victimRb == null)
                    victimRb = movement.GetComponentInParent<Rigidbody>();

                // Undo the contact shove on the owning machine before the knockout
                // message re-applies a tunable VelocityChange.
                if (
                    movement.isLocalPlayer
                    && victimRb != null
                    && collision.contactCount > 0
                )
                {
                    // collision.impulse is the impulse applied to THIS body (the ball);
                    // the player received the opposite. Adding it back cancels their shove.
                    victimRb.AddForce(collision.impulse, ForceMode.Impulse);
                }

                // Prevent follow-up contacts from re-shoving after the undo.
                IgnoreVictimColliders(movement.transform);

                var throwerIdentity = ThrowerInfo.GetComponent<NetworkIdentity>();
                uint throwerNetId = throwerIdentity != null ? throwerIdentity.netId : 0u;

                float dist = Vector3.Distance(ThrowerInfo.transform.position, hitPosition);
                var useId = new ItemUseId(
                    ThrowerInfo.PlayerId.Guid,
                    BlackHoleGrenadeItem.NextUseIndex(),
                    ItemType.RocketLauncher,
                    false
                );

                var knockoutMsg = new BowlingBallKnockoutMessage
                {
                    ThrowerNetId = throwerNetId,
                    LocalHitPoint = movement.transform.InverseTransformPoint(hitPosition),
                    Distance = dist,
                    IncomingVelocity = _rb.linearVelocity,
                    ItemUseId = useId,
                };

                // Prefer the owning connection; fall back to SendToAll so a listen-host
                // victim (localConnection) still receives the message.
                if (victimIdentity.connectionToClient != null)
                    victimIdentity.connectionToClient.Send(knockoutMsg);
                else
                    NetworkServer.SendToAll(knockoutMsg);

                return;
            }

            // ── Target dummies / other non-player Hittables ───────────────────
            // Guns call Hittable.HitWithItem, which raises WasHitByItem — the event
            // TargetDummy listens to for its flinch/spin animations. Without this,
            // a physics collision alone never triggers that reaction.
            var hittable = collision.gameObject.GetComponentInParent<Hittable>();
            if (hittable == null)
                return;

            // Players are handled above; HitWithItem on a player would stack a second
            // gun-style hit response on top of the knockout.
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

            var hitUseId = new ItemUseId(
                ThrowerInfo.PlayerId.Guid,
                BlackHoleGrenadeItem.NextUseIndex(),
                ItemType.RocketLauncher,
                false
            );

            // HitWithItem runs HitWithItemInternal locally, then tries CmdHitWithItem
            // to Rpc remotes. On a listen host that Cmd shortcut works. On a dedicated
            // server SendCommandInternal is a no-op (no active client), so we manually
            // broadcast the same Rpc path afterward.
            hittable.HitWithItem(
                ItemType.RocketLauncher,
                hitUseId,
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
                    hitUseId,
                    localHitPoint,
                    hitDirection,
                    localOrigin,
                    distance,
                    inventory,
                    NetworkTime.time
                );
        }

        /// <summary>
        /// Stops further PhysX contacts with a player after the first hit so the
        /// listen-host undo is not immediately overwritten by a multi-contact pulse.
        /// </summary>
        private void IgnoreVictimColliders(Transform victimRoot)
        {
            if (victimRoot == null)
                return;

            if (_ballColliders == null || _ballColliders.Length == 0)
                _ballColliders = GetComponentsInChildren<Collider>(true);

            var victimCols = victimRoot.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < _ballColliders.Length; i++)
            {
                var ballCol = _ballColliders[i];
                if (ballCol == null)
                    continue;

                for (int j = 0; j < victimCols.Length; j++)
                {
                    var victimCol = victimCols[j];
                    if (victimCol == null)
                        continue;

                    Physics.IgnoreCollision(ballCol, victimCol, true);
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
