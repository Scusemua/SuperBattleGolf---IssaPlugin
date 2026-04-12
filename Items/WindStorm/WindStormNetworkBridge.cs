using System.Collections;
using System.Collections.Generic;
using IssaPlugin.Overlays;
using Mirror;
using UnityEngine;

namespace IssaPlugin.Items
{
    /// <summary>
    /// Attached to every player object via NetworkBridgePatches.
    ///
    /// Flow:
    ///   1. Local player uses Wind Storm → OnUse sends WindStormActivateMessage to server.
    ///   2. Server validates, consumes the item, overrides WindManager wind speed to storm level,
    ///      and broadcasts WindStormBeginMessage(activatorNetId, duration, speed) to all clients.
    ///   3. Each client adds activatorNetId to WindImmuneNetIds so the Harmony patch can suppress
    ///      wind on that player's golf ball.
    ///   4. After duration the server restores the original wind speed and broadcasts WindStormEndMessage.
    ///   5. All clients remove the immunity entry.
    /// </summary>
    public class WindStormNetworkBridge : NetworkBridgeBase
    {
        // ── Global server lock — only one storm session at a time ─────────────
        private static bool _globalSessionActive;
        private static Coroutine _timeoutCoroutine;
        private static WindStormNetworkBridge _activeInstance;
        private static int _savedWindSpeed;
        private static int _savedWindAngle;

        // ── Client: netIds of players whose balls are immune to wind ──────────
        /// Read by WindStormPatches every physics frame — keep it a HashSet for O(1) lookup.
        public static readonly HashSet<uint> WindImmuneNetIds = new HashSet<uint>();

        /// True while a wind storm session is active on this client or server.
        public static bool IsSessionActive => _globalSessionActive;

        // ================================================================
        //  Server activation (called from NetworkManagerPatches handler)
        // ================================================================

        public void ServerActivate()
        {
            if (!isServer)
                return;

            if (_globalSessionActive)
            {
                IssaPluginPlugin.Log.LogWarning("[WindStorm] A session is already active.");
                return;
            }

            var inventory = GetComponent<PlayerInventory>();
            if (inventory == null)
                return;

            if (inventory.GetEffectivelyEquippedItem(true) != ItemRegistry.WindStormItemType)
            {
                IssaPluginPlugin.Log.LogWarning(
                    "[WindStorm] Player does not have Wind Storm item equipped."
                );
                return;
            }

            ItemHelper.ConsumeEquippedItem(inventory);

            _globalSessionActive = true;
            _activeInstance = this;

            // Save current wind state so we can restore it when the storm ends.
            _savedWindSpeed = WindManager.CurrentWindSpeed;
            if (SingletonNetworkBehaviour<WindManager>.HasInstance)
                _savedWindAngle = SingletonNetworkBehaviour<WindManager>
                    .Instance
                    .NetworkcurrentWindAngle;

            int stormSpeed = (int)ModConfig.WindStorm.StormSpeed.Value;
            int stormAngle = Random.Range(0, 360);

            if (SingletonNetworkBehaviour<WindManager>.HasInstance)
            {
                SingletonNetworkBehaviour<WindManager>.Instance.NetworkcurrentWindSpeed =
                    stormSpeed;
                SingletonNetworkBehaviour<WindManager>.Instance.NetworkcurrentWindAngle =
                    stormAngle;
            }

            float duration = ModConfig.WindStorm.Duration.Value;
            NetworkServer.SendToAll(
                new WindStormBeginMessage
                {
                    // Send 0 when ExcludeActivator is off so HandleBegin never adds
                    // anyone to WindImmuneNetIds and the storm hits everyone equally.
                    ActivatorNetId = ModConfig.WindStorm.ExcludeActivator.Value ? netId : 0u,
                    Duration = duration,
                    StormSpeed = stormSpeed,
                }
            );
            _timeoutCoroutine = StartCoroutine(ServerStormRoutine(duration, stormAngle));

            IssaPluginPlugin.Log.LogInfo(
                $"[WindStorm] Session started: speed={stormSpeed}, angle={stormAngle}, duration={duration}s."
            );
        }

        // ================================================================
        //  Client message handlers (registered in NetworkManagerPatches)
        // ================================================================

        public static void HandleBegin(WindStormBeginMessage msg)
        {
            if (msg.ActivatorNetId != 0u)
                WindImmuneNetIds.Add(msg.ActivatorNetId);
            WindStormOverlay.Instance?.SetActive(true, msg.Duration);
            IssaPluginPlugin.Log.LogInfo(
                $"[WindStorm] Storm began: speed={msg.StormSpeed}, immune netId={msg.ActivatorNetId}."
            );
        }

        public static void HandleEnd(WindStormEndMessage msg)
        {
            WindImmuneNetIds.Clear();
            WindStormOverlay.Instance?.SetActive(false);
            IssaPluginPlugin.Log.LogInfo("[WindStorm] Storm ended.");
        }

        // ================================================================
        //  Server internals
        // ================================================================

        private IEnumerator ServerStormRoutine(float duration, int initialAngle)
        {
            float elapsed = 0f;
            float interval = ModConfig.WindStorm.DirectionChangeInterval.Value;
            int range = (int)ModConfig.WindStorm.DirectionChangeRange.Value;
            int angle = initialAngle;

            while (elapsed < duration)
            {
                // Clamp the last wait so we don't overshoot the duration.
                float waitTime = Mathf.Min(interval, duration - elapsed);
                yield return new WaitForSeconds(waitTime);
                elapsed += waitTime;

                if (!_globalSessionActive)
                    yield break;

                // Shift the direction unless this was just the final timing-alignment wait.
                if (elapsed < duration && SingletonNetworkBehaviour<WindManager>.HasInstance)
                {
                    angle = (angle + Random.Range(-range, range + 1) + 360) % 360;
                    SingletonNetworkBehaviour<WindManager>.Instance.NetworkcurrentWindAngle = angle;
                }
            }

            if (SingletonNetworkBehaviour<WindManager>.HasInstance)
            {
                SingletonNetworkBehaviour<WindManager>.Instance.NetworkcurrentWindSpeed =
                    _savedWindSpeed;
                SingletonNetworkBehaviour<WindManager>.Instance.NetworkcurrentWindAngle =
                    _savedWindAngle;
            }

            NetworkServer.SendToAll(new WindStormEndMessage());
            _globalSessionActive = false;
            _activeInstance = null;
            _timeoutCoroutine = null;
            IssaPluginPlugin.Log.LogInfo("[WindStorm] Session ended, wind restored.");
        }

        // ================================================================
        //  Hole / server cleanup
        // ================================================================

        public static void GlobalServerHoleCleanup()
        {
            if (!_globalSessionActive)
                return;
            if (_timeoutCoroutine != null && _activeInstance != null)
                _activeInstance.StopCoroutine(_timeoutCoroutine);
            _timeoutCoroutine = null;
            _activeInstance = null;

            if (SingletonNetworkBehaviour<WindManager>.HasInstance)
            {
                SingletonNetworkBehaviour<WindManager>.Instance.NetworkcurrentWindSpeed =
                    _savedWindSpeed;
                SingletonNetworkBehaviour<WindManager>.Instance.NetworkcurrentWindAngle =
                    _savedWindAngle;
            }

            NetworkServer.SendToAll(new WindStormEndMessage());
            _globalSessionActive = false;
            IssaPluginPlugin.Log.LogInfo("[WindStorm] Session force-ended for hole transition.");
        }

        public override void ServerHoleCleanup() => GlobalServerHoleCleanup();

        public override void ClientHoleCleanup() => WindImmuneNetIds.Clear();

        public override void OnStopServer()
        {
            if (!_globalSessionActive)
                return;
            if (_timeoutCoroutine != null && _activeInstance != null)
                _activeInstance.StopCoroutine(_timeoutCoroutine);
            _timeoutCoroutine = null;
            _activeInstance = null;
            _globalSessionActive = false;
            NetworkServer.SendToAll(new WindStormEndMessage());
            IssaPluginPlugin.Log.LogInfo("[WindStorm] Session ended on server stop.");
        }
    }
}
