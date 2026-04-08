using System.Collections.Generic;
using Mirror;
using UnityEngine;

namespace IssaPlugin.Items
{
    /// <summary>
    /// Server-only MonoBehaviour attached to the black hole grenade after
    /// NetworkServer.Spawn().  Drives three phases:
    ///
    ///   Flying   — Rigidbody is live; grenade arcs under gravity.  An
    ///              OverlapSphere poll each FixedUpdate checks for the first
    ///              solid contact with terrain/ground.
    ///
    ///   Suck     — Rigidbody is frozen (isKinematic = true).  Each FixedUpdate
    ///              finds all non-player Rigidbodies within SuckRadius (golf balls,
    ///              golf carts, etc.) and applies an inward acceleration that
    ///              intensifies near the center.  A BlackHoleGrenadeLandedMessage
    ///              is broadcast once so every client can apply the suction to
    ///              their own local player.
    ///
    ///   Done     — One-shot: finds all non-player Rigidbodies in SuckRadius,
    ///              applies random outward velocity, broadcasts
    ///              BlackHoleGrenadeSpitMessage so clients handle their own players,
    ///              then calls NetworkServer.Destroy.
    /// </summary>
    public class BlackHoleGrenadeBehaviour : MonoBehaviour
    {
        // ----------------------------------------------------------------
        //  Configuration (set by BlackHoleGrenadeNetworkBridge before Start)
        // ----------------------------------------------------------------

        public PlayerInfo ThrowerInfo;
        public ItemUseId ItemUseId;
        public float GraceTime = 0.35f;
        public float SuckDuration = 6f;
        public float SuckRadius = 35f;

        /// Suction acceleration (m/s²) at the outer edge of the field.
        public float SuckForce = 8f;

        /// Suction acceleration (m/s²) right at the center.
        public float MaxSuckForce = 40f;
        public float SpitForce = 35f;
        public float StickRadius = 0.55f;

        // ----------------------------------------------------------------
        //  State
        // ----------------------------------------------------------------

        private enum Phase
        {
            Flying,
            Suck,
            Done,
        }

        private Phase _phase = Phase.Flying;
        private Rigidbody _rb;
        private float _elapsed;
        private float _suckElapsed;
        private bool _landedMessageSent;
        private readonly HashSet<Rigidbody> _seenRigidbodies = new();

        // ----------------------------------------------------------------
        //  Lifecycle
        // ----------------------------------------------------------------

        private void Start()
        {
            if (!NetworkServer.active)
            {
                enabled = false;
                return;
            }

            _rb = GetComponent<Rigidbody>();
            if (_rb != null)
            {
                _rb.useGravity = true;
                _rb.isKinematic = false;
                _rb.freezeRotation = false;
            }
        }

        private void FixedUpdate()
        {
            if (!NetworkServer.active)
                return;

            _elapsed += Time.fixedDeltaTime;

            switch (_phase)
            {
                case Phase.Flying:
                    UpdateFlying();
                    break;
                case Phase.Suck:
                    UpdateSuck();
                    break;
                case Phase.Done:
                    break;
            }
        }

        // ----------------------------------------------------------------
        //  Phase: Flying
        // ----------------------------------------------------------------

        private void UpdateFlying()
        {
            if (_elapsed < GraceTime)
                return;

            // Poll for ground contact using a tight overlap sphere.
            var groundHits = Physics.OverlapSphere(
                transform.position,
                StickRadius * 0.7f,
                ItemHelper.GroundLayerMask,
                QueryTriggerInteraction.Ignore
            );

            foreach (var col in groundHits)
            {
                if (col.transform.IsChildOf(transform) || col.transform == transform)
                    continue;

                Land();
                return;
            }
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (_phase != Phase.Flying)
                return;

            if ((ItemHelper.GroundLayerMask & (1 << collision.gameObject.layer)) != 0)
                Land();
        }

        // ----------------------------------------------------------------
        //  Landing — Flying → Suck transition
        // ----------------------------------------------------------------

        private void Land()
        {
            _phase = Phase.Suck;

            if (_rb != null)
            {
                _rb.linearVelocity = Vector3.zero;
                _rb.angularVelocity = Vector3.zero;
                _rb.isKinematic = true;
                _rb.detectCollisions = false;
            }

            IssaPluginPlugin.Log.LogInfo(
                $"[BlackHoleGrenade] Landed at {transform.position:F1} — starting suction."
            );
        }

        // ----------------------------------------------------------------
        //  Phase: Suck
        // ----------------------------------------------------------------

        private void UpdateSuck()
        {
            // Broadcast landed message on the first suck frame so clients start
            // their own suction coroutines.
            if (!_landedMessageSent)
            {
                _landedMessageSent = true;
                var ni = GetComponent<NetworkIdentity>();
                NetworkServer.SendToAll(
                    new BlackHoleGrenadeLandedMessage
                    {
                        BlackHoleNetId = ni != null ? ni.netId : 0u,
                        BlackHolePosition = new Vector3(
                            transform.position.x,
                            transform.position.y + 5f,
                            transform.position.z
                        ),
                        ThrowerInfo = ThrowerInfo,
                        SuckDuration = SuckDuration,
                        SuckRadius = SuckRadius,
                        SuckForce = SuckForce,
                        MaxSuckForce = MaxSuckForce,
                    }
                );
            }

            ApplySuctionToNonPlayers();

            _suckElapsed += Time.fixedDeltaTime;
            if (_suckElapsed >= SuckDuration)
                Spit();
        }

        // ----------------------------------------------------------------
        //  Suction (server-side, non-player physics objects only)
        // ----------------------------------------------------------------

        private void ApplySuctionToNonPlayers()
        {
            Vector3 center = transform.position;
            Vector3 blackHolePos = new Vector3(center.x, center.y + 5f, center.z);

            // Use ~0 (all layers) so golf balls, golf carts, dropped items, and
            // anything else with a Rigidbody are caught regardless of their layer.
            // RocketHittablesMask / GunHittablesMask do not cover all relevant layers
            // (e.g. golf balls are typically on Default or a dedicated ball layer).
            // The Rigidbody null-check and player filter below handle exclusions.
            var hits = Physics.OverlapSphere(
                blackHolePos,
                SuckRadius,
                Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore
            );

            _seenRigidbodies.Clear();

            foreach (var col in hits)
            {
                // Skip self
                if (col.transform.IsChildOf(transform) || col.transform == transform)
                    continue;

                // Skip players — clients handle their own player physics.
                if (col.GetComponentInParent<PlayerInfo>() != null)
                    continue;

                // Skip golf balls if the config says not to affect them.
                if (
                    !ModConfig.BlackHoleGrenade.AffectsGolfBalls.Value
                    && col.GetComponentInParent<GolfBall>() != null
                )
                    continue;

                // Skip golf carts that have a driver: the driver's client has
                // authority over the cart's Rigidbody and handles suction itself.
                // Empty carts (no driver) are server-authoritative, so handle them here.
                var cart = col.GetComponentInParent<GolfCartInfo>();
                if (cart != null && cart.NetworkdriverSeatReserver != null)
                    continue;

                var rb = col.attachedRigidbody;
                if (rb == null || _seenRigidbodies.Contains(rb))
                    continue;
                _seenRigidbodies.Add(rb);

                Vector3 toCenter = blackHolePos - rb.position;
                float dist = toCenter.magnitude;
                if (dist < 0.01f)
                    continue;

                float bonusForceMult = 1.0f;
                if (col.GetComponentInParent<GolfCartInfo>() != null)
                {
                    bonusForceMult = ModConfig.BlackHoleGrenade.BonusGolfCartForceMultiplier.Value;
                }

                // Quadratic falloff — matches the client-side curve so proximity
                // scaling feels consistent for all object types.
                float t = 1f - dist / SuckRadius;
                t *= t;
                float intensity = Mathf.Lerp(SuckForce, MaxSuckForce, t) * bonusForceMult;

                rb.AddForce(toCenter.normalized * intensity, ForceMode.Acceleration);

                // Suppress the bear's AI velocity writes so the suction force is
                // not overwritten by MoveInDirection on the bear's next FixedUpdate.
                rb.GetComponent<BearBehaviour>()?.NotifyBlackHoleSuction();
            }
        }

        // ----------------------------------------------------------------
        //  Spit — eject everything and destroy
        // ----------------------------------------------------------------

        public void Spit()
        {
            if (_phase == Phase.Done)
                return;
            _phase = Phase.Done;

            Vector3 center = transform.position;

            IssaPluginPlugin.Log.LogInfo(
                $"[BlackHoleGrenade] Spit phase at {center:F1} — ejecting objects."
            );

            // Apply random outward velocity to all non-player rigidbodies in range.
            var hits = Physics.OverlapSphere(
                center,
                SuckRadius,
                Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore
            );

            _seenRigidbodies.Clear();

            foreach (var col in hits)
            {
                if (col.transform.IsChildOf(transform) || col.transform == transform)
                    continue;
                if (col.GetComponentInParent<PlayerInfo>() != null)
                    continue;
                if (
                    !ModConfig.BlackHoleGrenade.AffectsGolfBalls.Value
                    && col.GetComponentInParent<GolfBall>() != null
                )
                    continue;

                var rb = col.attachedRigidbody;
                if (rb == null || _seenRigidbodies.Contains(rb))
                    continue;
                _seenRigidbodies.Add(rb);

                // Random direction biased upward so objects fly into the air.
                Vector3 dir = Random.onUnitSphere;
                dir.y = Mathf.Abs(dir.y) + 0.5f;
                dir = dir.normalized;

                // Suppress the bear's AI for long enough that the spit velocity
                // carries before MoveInDirection can overwrite it.
                rb.GetComponent<BearBehaviour>()?.NotifyBlackHoleSpitLaunch();

                rb.linearVelocity = dir * SpitForce;
            }

            // Broadcast to all clients so they apply the spit to their local player.
            var ni = GetComponent<NetworkIdentity>();
            NetworkServer.SendToAll(
                new BlackHoleGrenadeSpitMessage
                {
                    BlackHoleNetId = ni != null ? ni.netId : 0u,
                    BlackHolePosition = center,
                    SpitRadius = SuckRadius,
                    SpitForce = SpitForce,
                    ThrowerInfo = ThrowerInfo,
                    ItemUseId = ItemUseId,
                    BlackHoleGrenadeSpitVfxScale = ModConfig.BlackHoleGrenade.SpitVfxScale.Value,
                }
            );

            IssaPluginPlugin.Log.LogInfo("[BlackHoleGrenade] Spit complete — destroying.");
            NetworkServer.Destroy(gameObject);
        }
    }
}
