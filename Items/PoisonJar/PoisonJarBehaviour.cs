using System.Collections.Generic;
using Mirror;
using UnityEngine;

namespace IssaPlugin.Items
{
    /// <summary>
    /// Server-only MonoBehaviour attached to the thrown poison jar.
    ///
    /// Phase 1 — Flying: Rigidbody propels the jar as a ballistic projectile.
    ///   FixedUpdate polls OverlapSphere against the ground layer; after a short
    ///   grace period to clear the thrower's own colliders, the first contact
    ///   triggers landing.
    ///
    /// Phase 2 — Landed: Chooses who is poisoned (shielded players are skipped),
    ///   broadcasts PoisonJarLandedMessage so clients can spawn splash VFX, a
    ///   burst above each poisoned player, and the local overlay. The networked
    ///   jar object is then destroyed immediately.
    /// </summary>
    public class PoisonJarBehaviour : MonoBehaviour
    {
        /// NetId of the player who threw the jar (set by PoisonJarNetworkBridge before first tick).
        public uint ThrowerNetId;

        private Rigidbody _rb;
        private bool _landed;

        // Seconds before ground detection is enabled — prevents the jar from sticking
        // to the thrower's own colliders on the frame it spawns.
        private float _graceTimer = 0.25f;

        private void Start()
        {
            _rb = GetComponent<Rigidbody>();
        }

        private void FixedUpdate()
        {
            if (_landed)
                return;

            _graceTimer -= Time.fixedDeltaTime;
            if (_graceTimer > 0f)
                return;

            // Only check for landing once the jar is descending — avoids false positives
            // while the jar is still rising through geometry near the throw origin.
            if (_rb != null && _rb.linearVelocity.y > 0f)
                return;

            // Check for ground / terrain contact (0.4 m radius matches a typical grenade size)
            // if (Physics.CheckSphere(transform.position, 0.4f, ItemHelper.GroundLayerMask))
            //     Land();
        }

        private void OnCollisionEnter(Collision collision)
        {
            Land();
        }

        private void Land()
        {
            if (_landed)
                return;
            _landed = true;

            if (_rb != null)
            {
                _rb.isKinematic = true;
                _rb.linearVelocity = Vector3.zero;
                _rb.angularVelocity = Vector3.zero;
            }

            float radius = ModConfig.PoisonJar.Radius.Value;
            float duration = ModConfig.PoisonJar.Duration.Value;
            Vector3 origin = transform.position;

            NetworkServer.SendToAll(
                new PoisonJarLandedMessage
                {
                    Position = origin,
                    Radius = radius,
                    Duration = duration,
                    ThrowerNetId = ThrowerNetId,
                    PoisonedNetIds = CollectPoisonedNetIds(origin, radius),
                }
            );

            NetworkServer.Destroy(gameObject);
        }

        /// <summary>
        /// Players inside the cloud, excluding anyone whose electromagnetic shield
        /// is up. Shield state is read here, on the server, so every client shows
        /// the burst on the same people.
        /// </summary>
        private static uint[] CollectPoisonedNetIds(Vector3 origin, float radius)
        {
            float radiusSqr = radius * radius;
            var poisoned = new List<uint>();

            foreach (var entry in NetworkServer.spawned)
            {
                var identity = entry.Value;
                if (identity == null)
                    continue;

                var info = identity.GetComponent<PlayerInfo>();
                if (info == null)
                    continue;

                if ((info.transform.position - origin).sqrMagnitude > radiusSqr)
                    continue;

                if (info.IsElectromagnetShieldActive)
                {
                    Vector3 hitDir = (info.transform.position - origin).normalized;
                    info.PlayElectromagnetShieldHitForAllClients(hitDir);
                    continue;
                }

                poisoned.Add(identity.netId);
            }

            return poisoned.ToArray();
        }
    }
}
