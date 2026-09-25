using System.Collections;
using IssaPlugin.Overlays;
using Mirror;
using UnityEngine;

namespace IssaPlugin.Items
{
    /// Attached to every player via NetworkBridgePatches.
    /// Use sends NightActivateMessage; the server consumes the item and broadcasts begin/end.
    public class NightNetworkBridge : NetworkBridgeBase
    {
        private static bool _globalSessionActive;
        private static Coroutine _timeoutCoroutine;
        private static NightNetworkBridge _activeInstance;
        private static float _sessionEndTime;
        private static uint _activatorNetId;

        public override void OnStartServer()
        {
            if (!_globalSessionActive || connectionToClient == null)
                return;

            float remaining = _sessionEndTime - Time.time;
            if (remaining <= 0f)
                return;

            connectionToClient.Send(
                new NightBeginMessage
                {
                    Duration = remaining,
                    ActivatorNetId = _activatorNetId,
                }
            );
        }

        public void ServerActivate()
        {
            if (!isServer)
                return;

            if (ModConfig.Global.ForceNightMode.Value)
            {
                IssaPluginPlugin.Log.LogWarning(
                    "[Night] ForceNightMode is on — the Night Time item does nothing."
                );
                return;
            }

            if (_globalSessionActive)
            {
                IssaPluginPlugin.Log.LogWarning("[Night] A night session is already active.");
                return;
            }

            var inventory = GetComponent<PlayerInventory>();
            if (inventory == null)
                return;

            if (inventory.GetEffectivelyEquippedItem(true) != ItemRegistry.NightItemType)
            {
                IssaPluginPlugin.Log.LogWarning("[Night] Player does not have Night Time equipped.");
                return;
            }

            ItemHelper.ConsumeEquippedItem(inventory);

            _globalSessionActive = true;
            _activeInstance = this;
            _activatorNetId = netId;
            float duration = ModConfig.Night.Duration.Value;
            _sessionEndTime = Time.time + duration;
            NetworkServer.SendToAll(
                new NightBeginMessage { Duration = duration, ActivatorNetId = _activatorNetId }
            );
            _timeoutCoroutine = StartCoroutine(ServerTimeoutRoutine(duration));
            IssaPluginPlugin.Log.LogInfo($"[Night] Session started for {duration}s.");
        }

        public static void HandleBegin(NightBeginMessage msg)
        {
            if (ModConfig.Global.ForceNightMode.Value)
                return;

            bool exempt =
                ModConfig.Night.ExcludeActivator.Value
                && NetworkClient.localPlayer != null
                && NetworkClient.localPlayer.netId == msg.ActivatorNetId;

            if (!exempt)
            {
                NightItem.IsActive = true;
                NightLighting.Begin();
            }

            NightOverlay.Instance?.SetActive(true, msg.Duration);
            IssaPluginPlugin.Log.LogInfo(
                exempt
                    ? "[Night] Night started. Local player keeps normal lighting."
                    : "[Night] Night lighting started."
            );
        }

        public static void HandleEnd(NightEndMessage msg)
        {
            EndClient();
            IssaPluginPlugin.Log.LogInfo("[Night] Night lighting ended.");
        }

        private IEnumerator ServerTimeoutRoutine(float duration)
        {
            yield return new WaitForSeconds(duration);
            if (!_globalSessionActive)
                yield break;
            NetworkServer.SendToAll(new NightEndMessage());
            ClearServerSession();
            IssaPluginPlugin.Log.LogInfo("[Night] Session ended.");
        }

        public override void ServerHoleCleanup() => EndServerSession("hole transition");

        public override void ClientHoleCleanup() => EndClient();

        public override void OnStopServer()
        {
            // Only the activator's bridge owns the session. Another player
            // disconnecting must not turn night off for everyone.
            if (_activeInstance != this)
                return;
            EndServerSession("activator left");
        }

        private static void EndServerSession(string reason)
        {
            if (!_globalSessionActive)
                return;
            if (_timeoutCoroutine != null && _activeInstance != null)
                _activeInstance.StopCoroutine(_timeoutCoroutine);
            NetworkServer.SendToAll(new NightEndMessage());
            ClearServerSession();
            IssaPluginPlugin.Log.LogInfo($"[Night] Session force-ended ({reason}).");
        }

        private static void ClearServerSession()
        {
            _globalSessionActive = false;
            _activeInstance = null;
            _timeoutCoroutine = null;
            _activatorNetId = 0u;
            _sessionEndTime = 0f;
        }

        private static void EndClient()
        {
            NightItem.IsActive = false;
            NightLighting.End();
            NightOverlay.Instance?.SetActive(false);
        }
    }
}
