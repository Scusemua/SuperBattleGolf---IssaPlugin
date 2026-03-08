using IssaPlugin.Items;
using Mirror;
using UnityEngine;

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
        // Everything here runs on the server
        StartCoroutine(
            StealthBomberItem.ServerBombingPhase(
                GetComponent<PlayerInventory>(),
                equippedIndex,
                new StealthBomberItem.BombingStripInfo
                {
                    Center = center,
                    Forward = forward,
                    Length = length,
                }
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
        // Runs on all clients — spawns the visual locally
        StealthBomberItem.LocalSpawnBomberVisual(spawnPos, exitPos, direction, speed);
    }
}
