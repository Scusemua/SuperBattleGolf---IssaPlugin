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
            RpcPlayAC130Sound();
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

        [Command]
        public void CmdPlayAC130Sound()
        {
            RpcPlayAC130Sound();
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

        [ClientRpc(includeOwner = true)]
        private void RpcPlayAC130Sound()
        {
            var clip = AssetLoader.AC130AboveClip;
            if (clip == null)
            {
                IssaPluginPlugin.Log.LogWarning("[AC130] Audio clip not loaded.");
                return;
            }

            // PlayClipAtPoint uses 3D spatial audio, so if the AudioListener is
            // on a camera that is disabled (or moved far away) the clip is
            // silenced by distance rolloff. Use a dedicated 2D AudioSource
            // instead so the sound is always audible at full volume regardless
            // of listener position.
            var go = new GameObject("AC130_Sound");
            var src = go.AddComponent<AudioSource>();
            src.clip = clip;
            src.spatialBlend = 0f; // 0 = fully 2D, no distance rolloff
            src.volume = 1f;
            src.Play();

            Destroy(go, clip.length + 0.1f);
            IssaPluginPlugin.Log.LogInfo("[AC130] Playing ac130_above sound.");
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
