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
    /// RpcBeginLowGravity to all clients. After the configured duration the server calls
    /// RpcEndLowGravity to restore everything.
    /// </summary>
    public class LowGravityNetworkBridge : NetworkBehaviour
    {
        // ================================================================
        //  Global server lock — only one low-gravity session may run at a time.
        // ================================================================
        private static bool _globalSessionActive;

        // ================================================================
        //  Per-client saved render state (restored in RpcEndLowGravity)
        // ================================================================
        private Color _savedFogColor;
        private float _savedFogDensity;
        private Color _savedAmbientLight;
        private bool _savedFog;

        // ================================================================
        //  Client → Server
        // ================================================================

        [Command]
        public void CmdActivateLowGravity()
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
                IssaPluginPlugin.Log.LogWarning("[LowGravity] Player does not have Low Gravity item equipped.");
                return;
            }

            ItemHelper.ConsumeEquippedItem(inventory);

            _globalSessionActive = true;
            float duration = Configuration.LowGravityDuration.Value;
            RpcBeginLowGravity(duration);
            StartCoroutine(ServerTimeoutRoutine(duration));

            IssaPluginPlugin.Log.LogInfo($"[LowGravity] Server session started for {duration}s.");
        }

        // ================================================================
        //  Server → All clients
        // ================================================================

        [ClientRpc]
        private void RpcBeginLowGravity(float duration)
        {
            // Save current render settings so we can restore them exactly.
            _savedFogColor = RenderSettings.fogColor;
            _savedFogDensity = RenderSettings.fogDensity;
            _savedAmbientLight = RenderSettings.ambientLight;
            _savedFog = RenderSettings.fog;

            // Subtle space-like atmosphere: deep violet tint, faint haze.
            RenderSettings.fog = true;
            RenderSettings.fogColor = new Color(0.05f, 0.02f, 0.15f, 1f);
            RenderSettings.fogDensity = 0.01f;
            RenderSettings.ambientLight = new Color(0.2f, 0.1f, 0.4f);

            LowGravityItem.IsActive = true;
            LowGravityOverlay.Instance?.SetActive(true, duration);

            IssaPluginPlugin.Log.LogInfo("[LowGravity] Client session started.");
        }

        [ClientRpc]
        private void RpcEndLowGravity()
        {
            // Restore all saved render settings.
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
            RpcEndLowGravity();
            _globalSessionActive = false;
            IssaPluginPlugin.Log.LogInfo("[LowGravity] Server session ended.");
        }

        public override void OnStopServer()
        {
            // If the server stops while a session is active, reset the lock
            // so the next session can start cleanly.
            if (_globalSessionActive)
            {
                _globalSessionActive = false;
                IssaPluginPlugin.Log.LogInfo("[LowGravity] Global lock released on server stop.");
            }
        }
    }
}
