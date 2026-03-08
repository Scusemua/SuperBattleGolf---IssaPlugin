using System.Collections;
using Mirror;
using UnityEngine;

namespace IssaPlugin.Items
{
    /// <summary>
    /// Attached to the player object via NetworkBridgePatches.
    /// Handles AC130 activation, fire Commands, and session lifecycle RPCs.
    /// </summary>
    public class AC130NetworkBridge : NetworkBehaviour
    {
        private Coroutine _serverTimeout;

        // ================================================================
        //  Client → Server
        // ================================================================

        [Command]
        public void CmdStartAC130()
        {
            if (AC130Item.IsActive)
            {
                IssaPluginPlugin.Log.LogWarning("[AC130] Session already active.");
                return;
            }

            var inventory = GetComponent<PlayerInventory>();
            if (inventory == null)
                return;

            var equipped = inventory.GetEffectivelyEquippedItem(true);
            if (equipped != AC130Item.AC130ItemType)
            {
                IssaPluginPlugin.Log.LogWarning("[AC130] Player does not have AC130 equipped.");
                return;
            }

            ItemHelper.ConsumeEquippedItem(inventory);
            AC130Item.SetActive(true);

            TargetBeginAC130(connectionToClient);
            _serverTimeout = StartCoroutine(ServerTimeoutRoutine());

            IssaPluginPlugin.Log.LogInfo("[AC130] Server session started.");
        }

        [Command]
        public void CmdEndAC130()
        {
            EndServerSession();
        }

        [Command]
        public void CmdFireAC130(Vector3 position, Vector3 aimDirection)
        {
            if (!AC130Item.IsActive)
                return;

            var inventory = GetComponent<PlayerInventory>();
            if (inventory == null)
                return;

            float jitterDeg = Configuration.AC130RocketAngularJitter.Value;
            Quaternion jitter = Quaternion.Euler(
                Random.Range(-jitterDeg, jitterDeg),
                Random.Range(-jitterDeg, jitterDeg),
                0f
            );

            Quaternion fireRotation = Quaternion.LookRotation(aimDirection, Vector3.up);
            AC130Item.SpawnRocketInDirection(inventory, position, jitter * fireRotation);
        }

        // ================================================================
        //  Server → Client
        // ================================================================

        [TargetRpc]
        public void TargetBeginAC130(NetworkConnection target)
        {
            StartCoroutine(AC130Item.RunLocalSession(GetComponent<PlayerInventory>(), this));
        }

        [TargetRpc]
        public void TargetEndAC130(NetworkConnection target)
        {
            AC130Item.ForceEndLocalSession();
        }

        // ================================================================
        //  Server internals
        // ================================================================

        private IEnumerator ServerTimeoutRoutine()
        {
            yield return new WaitForSeconds(Configuration.AC130Duration.Value + 5f);
            if (AC130Item.IsActive)
                EndServerSession();
        }

        private void EndServerSession()
        {
            if (!AC130Item.IsActive)
                return;

            if (_serverTimeout != null)
            {
                StopCoroutine(_serverTimeout);
                _serverTimeout = null;
            }

            AC130Item.SetActive(false);
            TargetEndAC130(connectionToClient);
            IssaPluginPlugin.Log.LogInfo("[AC130] Server session ended.");
        }
    }
}
