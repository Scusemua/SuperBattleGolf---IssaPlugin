using System.Collections;
using Mirror;
using UnityEngine;

namespace IssaPlugin.Items
{
    /// Attached to the player object via NetworkBridgePatches.
    /// Handles bomber Command (client→server) and visual RPC (server→all clients).
    ///
    /// _isBombing is an instance field so concurrent bombing runs from different
    /// players don't block each other, while still preventing the same player
    /// from stacking multiple runs.
    public class BomberNetworkBridge : NetworkBehaviour
    {
        /// True on the server while this player's bombing coroutine is running.
        private bool _isBombing;

        /// <summary>
        /// Set by CmdPrepareBomberRocket when the owning client has the bomber
        /// locked on. Consumed by GunshipRocketHomingPatch (patch 4) when the
        /// next rocket spawns on the server.
        /// </summary>
        public bool PendingBomberHoming;

        [Command]
        public void CmdRequestBombingRun(
            Vector3 center,
            Vector3 forward,
            float length,
            int equippedIndex
        )
        {
            // Per-instance guard — prevents the same player stacking runs,
            // but does not affect other players' bridges.
            if (_isBombing)
            {
                IssaPluginPlugin.Log.LogWarning(
                    "[Bomber] Run already in progress for this player."
                );
                return;
            }

            _isBombing = true;

            StartCoroutine(
                StealthBomberItem.ServerBombingPhase(
                    GetComponent<PlayerInventory>(),
                    equippedIndex,
                    new StealthBomberItem.BombingStripInfo
                    {
                        Center = center,
                        Forward = forward,
                        Length = length,
                    },
                    this,
                    () => _isBombing = false
                )
            );
        }

        /// <summary>
        /// Called by the lock-on detection patch (GunshipLockOnDetectionPatch)
        /// while the local player has the bomber proxy locked on.
        /// Sets PendingBomberHoming so the next rocket that spawns homes toward it.
        /// </summary>
        [Command]
        public void CmdPrepareBomberRocket()
        {
            PendingBomberHoming = true;
        }

        /// <summary>
        /// Adds BomberMarker, Entity (safety), and LockOnTarget to the proxy on
        /// all clients so the game's lock-on system can detect and track it.
        /// Entity must be added before LockOnTarget so LockOnTarget.Awake()
        /// can cache AsEntity via GetComponent&lt;Entity&gt;().
        /// </summary>
        [ClientRpc(includeOwner = true)]
        public void RpcAddBomberLockOnComponents(NetworkIdentity proxyIdentity)
        {
            if (proxyIdentity == null)
                return;

            var go = proxyIdentity.gameObject;

            if (go.GetComponent<BomberMarker>() == null)
                go.AddComponent<BomberMarker>();

            if (go.GetComponent<Entity>() == null)
                go.AddComponent<Entity>();

            if (go.GetComponent<LockOnTarget>() == null)
                go.AddComponent<LockOnTarget>();
        }

        /// <summary>
        /// Fired on all clients when the bomber proxy is shot down.
        /// Disables the straight-flight behaviour on the local visual bomber,
        /// then hands control to the Rigidbody: applies forward momentum, a
        /// rocket-impact impulse, and a tumble torque so the bomber falls
        /// out of the sky under physics.
        /// </summary>
        [ClientRpc(includeOwner = true)]
        public void RpcBomberShotDown(
            Vector3 crashDir,
            float crashSpeed,
            Vector3 impactDir,
            Vector3 torqueImpulse
        )
        {
            IssaPluginPlugin.Log.LogInfo("[Bomber] Bomber shot down — triggering physics crash.");

            var visual = StealthBomberItem.ActiveBomberVisual;
            if (visual == null)
                return;

            // Disable straight-flight so it no longer drives transform.position.
            var fly = visual.GetComponent<StealthBomberItem.BomberFlyBehaviour>();
            float flySpeed = fly != null ? fly.speed : crashSpeed;
            if (fly != null)
                fly.enabled = false;

            // Disable all colliders to avoid the non-convex MeshCollider + non-kinematic
            // Rigidbody error, and to prevent unexpected terrain/geometry interactions
            // during the crash. Ground impact is detected by BomberCrashBehaviour.
            foreach (var col in visual.GetComponents<Collider>())
                col.enabled = false;

            var rb = visual.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.isKinematic = false;
                rb.useGravity = true;

                // Preserve the bomber's forward momentum at flight speed.
                rb.linearVelocity = crashDir * flySpeed;

                // Apply outward blast force from the explosion.
                if (impactDir != Vector3.zero)
                    rb.AddForce(
                        impactDir * Configuration.BomberCrashImpactForce.Value,
                        ForceMode.Impulse
                    );

                // Apply a continuous downward force in the global space
                rb.AddForce(Vector3.down * Configuration.BomberCrashDownwardForce.Value);

                // Apply server-computed tumble torque (same value on all clients).
                rb.AddTorque(torqueImpulse, ForceMode.Impulse);
            }

            var crashBehavior = visual.AddComponent<BomberCrashBehaviour>();
            crashBehavior.Rigidbody = rb;
        }

        [ClientRpc]
        public void RpcSpawnBomberVisual(
            Vector3 spawnPos,
            Vector3 exitPos,
            Vector3 direction,
            float speed
        )
        {
            StealthBomberItem.LocalSpawnBomberVisual(spawnPos, exitPos, direction, speed);
        }
    }
}
