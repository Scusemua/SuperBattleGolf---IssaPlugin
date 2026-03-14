using System.Collections;
using IssaPlugin.Overlays;
using Mirror;
using UnityEngine;

namespace IssaPlugin.Items
{
    /// <summary>
    /// Attached to every player object via NetworkBridgePatches.
    ///
    /// When the owning client uses the Low Gravity item, CmdActivateLowGravity is called
    /// on the server, which validates the request, consumes the item, and broadcasts
    /// LowGravityBeginMessage to all clients via NetworkServer.SendToAll (bypassing Mirror's
    /// IL-weaving requirement that [ClientRpc] methods have).
    /// </summary>
    public class LowGravityNetworkBridge : NetworkBehaviour
    {
        // ================================================================
        //  Global server lock — only one low-gravity session may run at a time.
        // ================================================================
        private static bool _globalSessionActive;

        // ================================================================
        //  Per-client saved render state — static because low-gravity is global
        //  and only one session is active at a time.
        // ================================================================
        private static Color _savedFogColor;
        private static float _savedFogDensity;
        private static Color _savedAmbientLight;
        private static bool _savedFog;

        // ================================================================
        //  Client → Server
        // ================================================================

        public void ServerActivateLowGravity()
        {
            if (_globalSessionActive)
            {
                IssaPluginPlugin.Log.LogWarning("[LowGravity] A session is already active.");
                return;
            }

            var inventory = GetComponent<PlayerInventory>();
            if (inventory == null)
                return;

            var equipped = inventory.GetEffectivelyEquippedItem(true);
            if (equipped != LowGravityItem.LowGravityItemType)
            {
                IssaPluginPlugin.Log.LogWarning(
                    "[LowGravity] Player does not have Low Gravity item equipped."
                );
                return;
            }

            ItemHelper.ConsumeEquippedItem(inventory);

            _globalSessionActive = true;
            float duration = Configuration.LowGravityDuration.Value;

            // [ClientRpc] is not IL-weaved in plugin DLLs — use NetworkMessage instead.
            NetworkServer.SendToAll(new LowGravityBeginMessage { Duration = duration });
            StartCoroutine(ServerTimeoutRoutine(duration));

            IssaPluginPlugin.Log.LogInfo($"[LowGravity] Server session started for {duration}s.");
        }

        // ================================================================
        //  Message handlers — registered in NetworkManagerPatches
        // ================================================================

        public static void HandleLowGravityBegin(LowGravityBeginMessage msg)
        {
            IssaPluginPlugin.Log.LogInfo($"[LowGravity] HandleLowGravityBegin called: msg={msg}");

            // Only save render state if not already active — repeated sessions would
            // otherwise overwrite the saved state with the already-modified fog values.
            if (!LowGravityItem.IsActive)
            {
                _savedFogColor = RenderSettings.fogColor;
                _savedFogDensity = RenderSettings.fogDensity;
                _savedAmbientLight = RenderSettings.ambientLight;
                _savedFog = RenderSettings.fog;
            }

            RenderSettings.fog = true;
            RenderSettings.fogColor = new Color(0.05f, 0.02f, 0.15f, 1f);
            RenderSettings.fogDensity = 0.01f;
            RenderSettings.ambientLight = new Color(0.2f, 0.1f, 0.4f);

            LowGravityItem.IsActive = true;
            LowGravityOverlay.Instance?.SetActive(true, msg.Duration);

            IssaPluginPlugin.Log.LogInfo("[LowGravity] Client session started.");
        }

        public static void HandleLowGravityEnd(LowGravityEndMessage msg)
        {
            IssaPluginPlugin.Log.LogInfo($"[LowGravity] HandleLowGravityEnd called: msg={msg}");

            RenderSettings.fogColor = _savedFogColor;
            RenderSettings.fogDensity = _savedFogDensity;
            RenderSettings.ambientLight = _savedAmbientLight;
            RenderSettings.fog = _savedFog;

            LowGravityItem.IsActive = false;
            LowGravityOverlay.Instance?.SetActive(false);

            IssaPluginPlugin.Log.LogInfo("[LowGravity] Client session ended.");
        }

        // ================================================================
        //  Server internals
        // ================================================================

        private IEnumerator ServerTimeoutRoutine(float duration)
        {
            yield return new WaitForSeconds(duration);
            NetworkServer.SendToAll(new LowGravityEndMessage());
            _globalSessionActive = false;
            IssaPluginPlugin.Log.LogInfo("[LowGravity] Server session ended.");
        }

        public override void OnStopServer()
        {
            if (_globalSessionActive)
            {
                NetworkServer.SendToAll(new LowGravityEndMessage());
                _globalSessionActive = false;
                IssaPluginPlugin.Log.LogInfo(
                    "[LowGravity] Session ended and global lock released on server stop."
                );
            }
        }
    }
}
