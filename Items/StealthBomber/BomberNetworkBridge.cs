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
        /// Disables the straight-flight behaviour on the local visual bomber and
        /// attaches BomberCrashBehaviour to trigger the crash dive.
        /// </summary>
        [ClientRpc(includeOwner = true)]
        public void RpcBomberShotDown(Vector3 crashDir)
        {
            IssaPluginPlugin.Log.LogInfo("[Bomber] Bomber shot down — triggering crash visual.");

            var visual = StealthBomberItem.ActiveBomberVisual;
            if (visual == null)
                return;

            // Disable straight-flight so both behaviours don't fight over position.
            var fly = visual.GetComponent<StealthBomberItem.BomberFlyBehaviour>();
            if (fly != null)
                fly.enabled = false;

            // Orient the visual in the crash direction before the dive takes over.
            if (crashDir != Vector3.zero)
                visual.transform.rotation = Quaternion.LookRotation(crashDir, Vector3.up);

            visual.AddComponent<BomberCrashBehaviour>();
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
