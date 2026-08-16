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
    /// FreezeBeginMessage to all clients via NetworkServer.SendToAll (bypassing Mirror's
    /// IL-weaving requirement that [ClientRpc] methods have).
    /// </summary>
    public class FreezeNetworkBridge : NetworkBridgeBase
    {
        // ================================================================
        //  Global server lock — only one freeze session may run at a time.
        // ================================================================
        private static bool _globalSessionActive;
        private static Coroutine _timeoutCoroutine;
        private static FreezeNetworkBridge _activeInstance;

        // ================================================================
        //  Per-client saved render state — static because freeze is global
        //  and only one session is active at a time.
        // ================================================================
        private static Color _savedFogColor;
        private static float _savedFogDensity;
        private static Color _savedAmbientLight;
        private static bool _savedFog;

        // ================================================================
        //  Client → Server
        // ================================================================

        public void ServerActivateFreeze()
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
            if (equipped != ItemRegistry.FreezeItemType)
            {
                IssaPluginPlugin.Log.LogWarning(
                    "[Freeze] Player does not have Freeze item equipped."
                );
                return;
            }

            ItemHelper.ConsumeEquippedItem(inventory);

            _globalSessionActive = true;
            _activeInstance = this;
            float duration = ModConfig.Freeze.Duration.Value;

            // [ClientRpc] is not IL-weaved in plugin DLLs — use NetworkMessage instead.
            NetworkServer.SendToAll(
                new FreezeBeginMessage { Duration = duration, ActivatorNetId = netId }
            );
            _timeoutCoroutine = StartCoroutine(ServerTimeoutRoutine(duration));

            IssaPluginPlugin.Log.LogInfo($"[Freeze] Server freeze started for {duration}s.");
        }

        // ================================================================
        //  Message handlers — registered in NetworkManagerPatches
        // ================================================================

        public static void HandleFreezeBegin(FreezeBeginMessage msg)
        {
            // Only save render state if not already frozen — repeated sessions would
            // otherwise overwrite the saved state with the already-modified fog values.
            if (!FreezeItem.IsFrozen)
            {
                _savedFogColor = RenderSettings.fogColor;
                _savedFogDensity = RenderSettings.fogDensity;
                _savedAmbientLight = RenderSettings.ambientLight;
                _savedFog = RenderSettings.fog;
            }

            RenderSettings.fog = true;
            RenderSettings.fogColor = new Color(0.7f, 0.85f, 1.0f, 1f);
            RenderSettings.fogDensity = 0.04f;
            RenderSettings.ambientLight = new Color(0.5f, 0.65f, 0.9f);

            bool isActivator =
                NetworkClient.localPlayer != null
                && NetworkClient.localPlayer.netId == msg.ActivatorNetId;
            bool exempt = isActivator && !ModConfig.Freeze.AffectsUser.Value;

            if (!exempt)
                FreezeItem.IsFrozen = true;

            FreezeOverlay.Instance?.SetFrozen(!exempt, msg.Duration);

            IssaPluginPlugin.Log.LogInfo(
                exempt
                    ? "[Freeze] Client freeze started (local player exempt as activator)."
                    : "[Freeze] Client freeze started."
            );
        }

        public static void HandleFreezeEnd(FreezeEndMessage msg)
        {
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
            NetworkServer.SendToAll(new FreezeEndMessage());
            _globalSessionActive = false;
            _activeInstance = null;
            _timeoutCoroutine = null;
            IssaPluginPlugin.Log.LogInfo("[Freeze] Server freeze session ended.");
        }

        // ── Hole transition cleanup ───────────────────────────────────────────

        public static void GlobalServerHoleCleanup()
        {
            if (!_globalSessionActive)
                return;
            if (_timeoutCoroutine != null && _activeInstance != null)
                _activeInstance.StopCoroutine(_timeoutCoroutine);
            _timeoutCoroutine = null;
            _activeInstance = null;
            NetworkServer.SendToAll(new FreezeEndMessage());
            _globalSessionActive = false;
            IssaPluginPlugin.Log.LogInfo("[Freeze] Session force-ended for hole transition.");
        }

        public override void ServerHoleCleanup()
        {
            GlobalServerHoleCleanup();
        }

        // Runs on client
        public override void ClientHoleCleanup() { }

        public override void OnStopServer()
        {
            if (_globalSessionActive)
            {
                if (_timeoutCoroutine != null && _activeInstance != null)
                    _activeInstance.StopCoroutine(_timeoutCoroutine);
                _timeoutCoroutine = null;
                _activeInstance = null;
                NetworkServer.SendToAll(new FreezeEndMessage());
                _globalSessionActive = false;
                IssaPluginPlugin.Log.LogInfo(
                    "[Freeze] Session ended and global lock released on server stop."
                );
            }
        }
    }
}
