using System.Collections;
using IssaPlugin.Overlays;
using Mirror;
using UnityEngine;

namespace IssaPlugin.Items
{
    /// <summary>
    /// Attached to every player object via NetworkBridgePatches.
    ///
    /// When the owning client uses the Freeze World item, CmdActivateFreeze is called
    /// on the server, which validates the request, consumes the item, and broadcasts
    /// RpcBeginFreeze to all clients. After the configured duration the server calls
    /// RpcEndFreeze to restore everything.
    /// </summary>
    public class FreezeNetworkBridge : NetworkBehaviour
    {
        // ================================================================
        //  Global server lock — only one freeze session may run at a time.
        // ================================================================
        private static bool _globalSessionActive;

        // ================================================================
        //  Per-client saved render state (restored in RpcEndFreeze)
        // ================================================================
        private Color _savedFogColor;
        private float _savedFogDensity;
        private Color _savedAmbientLight;
        private bool _savedFog;

        // ================================================================
        //  Client → Server
        // ================================================================

        [Command]
        public void CmdActivateFreeze()
        {
            if (_globalSessionActive)
            {
                IssaPluginPlugin.Log.LogWarning("[Freeze] A freeze session is already active.");
                return;
            }

            var inventory = GetComponent<PlayerInventory>();
            if (inventory == null)
                return;

            var equipped = inventory.GetEffectivelyEquippedItem(true);
            if (equipped != FreezeItem.FreezeItemType)
            {
                IssaPluginPlugin.Log.LogWarning("[Freeze] Player does not have Freeze item equipped.");
                return;
            }

            ItemHelper.ConsumeEquippedItem(inventory);

            _globalSessionActive = true;
            float duration = Configuration.FreezeDuration.Value;
            RpcBeginFreeze(duration);
            StartCoroutine(ServerTimeoutRoutine(duration));

            IssaPluginPlugin.Log.LogInfo($"[Freeze] Server freeze started for {duration}s.");
        }

        // ================================================================
        //  Server → All clients
        // ================================================================

        [ClientRpc]
        private void RpcBeginFreeze(float duration)
        {
            // Save current render settings so we can restore them exactly.
            _savedFogColor = RenderSettings.fogColor;
            _savedFogDensity = RenderSettings.fogDensity;
            _savedAmbientLight = RenderSettings.ambientLight;
            _savedFog = RenderSettings.fog;

            // Apply icy atmosphere.
            RenderSettings.fog = true;
            RenderSettings.fogColor = new Color(0.7f, 0.85f, 1.0f, 1f);
            RenderSettings.fogDensity = 0.04f;
            RenderSettings.ambientLight = new Color(0.5f, 0.65f, 0.9f);

            FreezeItem.IsFrozen = true;
            FreezeOverlay.Instance?.SetFrozen(true, duration);

            IssaPluginPlugin.Log.LogInfo("[Freeze] Client freeze started.");
        }

        [ClientRpc]
        private void RpcEndFreeze()
        {
            // Restore all saved render settings.
            RenderSettings.fogColor = _savedFogColor;
            RenderSettings.fogDensity = _savedFogDensity;
            RenderSettings.ambientLight = _savedAmbientLight;
            RenderSettings.fog = _savedFog;

            FreezeItem.IsFrozen = false;
            FreezeOverlay.Instance?.SetFrozen(false);

            IssaPluginPlugin.Log.LogInfo("[Freeze] Client freeze ended.");
        }

        // ================================================================
        //  Server internals
        // ================================================================

        private IEnumerator ServerTimeoutRoutine(float duration)
        {
            yield return new WaitForSeconds(duration);
            RpcEndFreeze();
            _globalSessionActive = false;
            IssaPluginPlugin.Log.LogInfo("[Freeze] Server freeze session ended.");
        }

        public override void OnStopServer()
        {
            // If the server stops while a freeze is active, reset the lock
            // so the next session can start cleanly.
            if (_globalSessionActive)
            {
                _globalSessionActive = false;
                IssaPluginPlugin.Log.LogInfo("[Freeze] Global lock released on server stop.");
            }
        }
    }
}
