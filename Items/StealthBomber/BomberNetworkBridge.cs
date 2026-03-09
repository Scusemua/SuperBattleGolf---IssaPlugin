using System.Collections;
using Mirror;
using UnityEngine;

namespace IssaPlugin.Items
{
    /// Attached to the player object via NetworkBridgePatches.
    /// Handles bomber Command (client→server) and visual RPC (server→all clients).
    public class BomberNetworkBridge : NetworkBehaviour
    {
        [Command]
        public void CmdRequestBombingRun(
            Vector3 center,
            Vector3 forward,
            float length,
            int equippedIndex
        )
        {
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
                    this
                )
            );
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
