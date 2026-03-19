using System.Reflection;
using Mirror;
using UnityEngine;

namespace IssaPlugin.Items
{
    /// <summary>
    /// Attached to the sticky grenade GameObject on the server immediately after
    /// NetworkServer.Spawn().  Drives three phases:
    ///
    ///   Flying   — Rigidbody is live; grenade arcs under gravity.  An
    ///              OverlapSphere poll each FixedUpdate checks for the first
    ///              solid contact (player, vehicle, terrain, golf cart).
    ///
    ///   Stuck    — Rigidbody is frozen (isKinematic = true).  Each FixedUpdate
    ///              the grenade follows its target's transform using a stored
    ///              local-space offset, so it rides players and moving vehicles
    ///              without parenting the NetworkTransform.  A StickyGrenadeStuckMessage
    ///              is broadcast once so all clients can mirror this visually.
    ///
    ///   Detonated — ServerExplode is reflected, StickyGrenadeDetonatedMessage is
    ///               broadcast, then NetworkServer.Destroy cleans up.
    ///
    /// The overlap-sphere polling approach is used instead of OnTriggerEnter/
    /// OnCollisionEnter because the grenade's layer / the game's collision matrix
    /// is not under our control; the poll uses GunHittablesMask for players and
    /// a broad all-layers sphere for terrain/props.
    /// </summary>
    public class StickyGrenadeBehaviour : NetworkBehaviour
    {
        // ----------------------------------------------------------------
        //  Configuration (set by StickyGrenadeNetworkBridge before first Update)
        // ----------------------------------------------------------------

        public PlayerInfo ThrowerInfo;
        public float GraceTime = 0.35f; // seconds before sticking is checked
        public float FuseTime = 3.5f;
        public float StickRadius = 0.55f; // overlap sphere radius for stick check
        public float ExplosionScale = 4.0f;

        // ----------------------------------------------------------------
        //  State
        // ----------------------------------------------------------------

        private enum Phase
        {
            Flying,
            Stuck,
            Detonated,
        }

        private Phase _phase = Phase.Flying;

        private Rigidbody _rb;
        private float _elapsed; // time since throw
        private float _fuseElapsed; // time since stuck

        // Stuck-target tracking
        private Transform _stickyTarget; // null = world-space (static geometry)
        private Vector3 _localOffset; // offset in _stickyTarget.localSpace (or world if null)
        private Vector3 _worldStuckPos; // fallback for static geometry

        // Reflected Rocket.ServerExplode
        private static readonly MethodInfo RocketServerExplode = typeof(Rocket).GetMethod(
            "ServerExplode",
            BindingFlags.NonPublic | BindingFlags.Instance
        );

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
            if (_rb == null)
            {
                var entity = GetComponent<Entity>();
                if (entity != null && entity.HasRigidbody)
                    _rb = entity.Rigidbody;
            }

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
                case Phase.Stuck:
                    UpdateStuck();
                    break;
                case Phase.Detonated:
                    break;
            }
        }

        // ----------------------------------------------------------------
        //  Phase: Flying
        // ----------------------------------------------------------------

        private void UpdateFlying()
        {
            // Grace window — don't try to stick immediately after throwing
            if (_elapsed < GraceTime)
                return;

            // ── Player / vehicle check ───────────────────────────────────
            // Use GunHittablesMask so we hit the same colliders that bullets do.
            var playerHits = Physics.OverlapSphere(
                transform.position,
                StickRadius,
                GameManager.LayerSettings.GunHittablesMask,
                QueryTriggerInteraction.Ignore
            );

            foreach (var col in playerHits)
            {
                // Skip the thrower during the grace window (already past, but
                // double-guard in case of unusual frame timing)
                var playerInfo = col.GetComponentInParent<PlayerInfo>();
                if (playerInfo != null)
                {
                    if (ThrowerInfo != null && playerInfo == ThrowerInfo)
                        continue;

                    StickTo(playerInfo.transform, playerInfo.GetComponent<NetworkIdentity>());
                    return;
                }

                // Custom vehicles — check for NetworkIdentity on or above the collider
                var ni = col.GetComponentInParent<NetworkIdentity>();
                if (ni != null && ni.GetComponent<PlayerInfo>() == null)
                {
                    // It's a networked non-player object (Donut, AC130 proxy, golf cart etc.)
                    StickTo(ni.transform, ni);
                    return;
                }
            }

            // ── Terrain / static geometry check ─────────────────────────
            // Broad overlap on Default + Terrain layers.
            var groundHits = Physics.OverlapSphere(
                transform.position,
                StickRadius * 0.7f, // tighter radius for ground to avoid false triggers
                ItemHelper.GroundLayerMask,
                QueryTriggerInteraction.Ignore
            );

            if (groundHits.Length > 0)
            {
                StickTo(null, null); // static world geometry — no moving parent
                return;
            }
        }

        // ----------------------------------------------------------------
        //  Phase: Stuck
        // ----------------------------------------------------------------

        private void UpdateStuck()
        {
            // Track moving target
            if (_stickyTarget != null)
                transform.position = _stickyTarget.TransformPoint(_localOffset);
            else
                transform.position = _worldStuckPos;

            _fuseElapsed += Time.fixedDeltaTime;

            if (_fuseElapsed >= FuseTime)
                Detonate();
        }

        // ----------------------------------------------------------------
        //  Stick logic
        // ----------------------------------------------------------------

        private void StickTo(Transform target, NetworkIdentity targetNetworkIdentity)
        {
            _phase = Phase.Stuck;

            if (_rb != null)
            {
                _rb.linearVelocity = Vector3.zero;
                _rb.angularVelocity = Vector3.zero;
                _rb.isKinematic = true;
            }

            _stickyTarget = target;

            uint targetNetId = 0;

            if (target != null)
            {
                // Store offset in the target's local space so it follows correctly
                _localOffset = target.InverseTransformPoint(transform.position);
                targetNetId = targetNetworkIdentity != null ? targetNetworkIdentity.netId : 0;

                IssaPluginPlugin.Log.LogInfo(
                    $"[StickyGrenade] Stuck to moving target netId={targetNetId} "
                        + $"localOffset={_localOffset}"
                );
            }
            else
            {
                // Static geometry — just hold world position
                _worldStuckPos = transform.position;

                IssaPluginPlugin.Log.LogInfo(
                    $"[StickyGrenade] Stuck to static geometry at {_worldStuckPos}"
                );
            }

            // Broadcast to all clients so they can visually track the target too
            var grenadeNi = GetComponent<NetworkIdentity>();
            NetworkServer.SendToAll(
                new StickyGrenadeStuckMessage
                {
                    GrenadeNetId = grenadeNi != null ? grenadeNi.netId : 0,
                    TargetNetId = targetNetId,
                    LocalOffset = target != null ? _localOffset : _worldStuckPos,
                    FuseRemaining = FuseTime,
                }
            );
        }

        // ----------------------------------------------------------------
        //  Detonation
        // ----------------------------------------------------------------

        public void Detonate()
        {
            if (_phase == Phase.Detonated)
                return;
            _phase = Phase.Detonated;

            Vector3 blastPos = transform.position;

            IssaPluginPlugin.Log.LogInfo($"[StickyGrenade] Detonating at {blastPos}");

            // Broadcast explosion event to all clients before destroying
            NetworkServer.SendToAll(new StickyGrenadeDetonatedMessage { WorldPosition = blastPos });

            // Apply explosion damage / knockback using a temporary Rocket stand-in.
            // We instantiate the rocket prefab, initialize it without Network.Spawn,
            // trigger ServerExplode, then immediately destroy it.  This ensures the
            // full game explosion pipeline (damage, knockback, VFX sync) runs normally.
            var tempRocket = Object.Instantiate(
                GameManager.ItemSettings.RocketPrefab,
                blastPos,
                Quaternion.identity
            );

            if (tempRocket != null)
            {
                // Build a minimal ItemUseId — the thrower's info was stored at throw time
                var useId = new ItemUseId(
                    ThrowerInfo?.PlayerId.Guid ?? 0UL,
                    StickyGrenadeItem.NextUseIndex(),
                    ItemType.RocketLauncher
                );
                tempRocket.ServerInitialize(ThrowerInfo, null, useId);
                NetworkServer.Spawn(tempRocket.gameObject);

                ExplosionScaler.Register(tempRocket, ExplosionScale);
                RocketServerExplode?.Invoke(tempRocket, new object[] { blastPos });
            }

            NetworkServer.Destroy(gameObject);
        }
    }
}
