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

            _isBombing = true;
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
