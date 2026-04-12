using System.Collections;
using System.Collections.Generic;
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

        // ── Client: netIds of players whose balls are immune to wind ──────────
        /// Read by WindStormPatches every physics frame — keep it a HashSet for O(1) lookup.
        public static readonly HashSet<uint> WindImmuneNetIds = new HashSet<uint>();

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

            // Save current speed so we can restore it when the storm ends.
            // The angle is intentionally left unchanged — wind direction stays the same
            // and only the speed is cranked up. Randomising the angle causes the
            // drag-based wind effect to land in an unpredictable direction relative to
            // the ball's flight path, often making the storm feel weaker than it is.
            _savedWindSpeed = WindManager.CurrentWindSpeed;

            int stormSpeed = (int)ModConfig.WindStorm.StormSpeed.Value;
            int currentAngle =
                WindManager.CurrentWindDirection != UnityEngine.Vector3.zero
                    ? (int)
                        UnityEngine
                            .Quaternion.LookRotation(WindManager.CurrentWindDirection)
                            .eulerAngles.y
                    : 0;

            if (SingletonNetworkBehaviour<WindManager>.HasInstance)
                SingletonNetworkBehaviour<WindManager>.Instance.NetworkcurrentWindSpeed =
                    stormSpeed;

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
            _timeoutCoroutine = StartCoroutine(ServerTimeoutRoutine(duration));

            IssaPluginPlugin.Log.LogInfo(
                $"[WindStorm] Session started: speed={stormSpeed}, angle={currentAngle}, duration={duration}s."
            );
        }

        // ================================================================
        //  Client message handlers (registered in NetworkManagerPatches)
        // ================================================================

        public static void HandleBegin(WindStormBeginMessage msg)
        {
            if (msg.ActivatorNetId != 0u)
                WindImmuneNetIds.Add(msg.ActivatorNetId);
            // TODO: trigger storm VFX / audio / overlay once assets are available
            IssaPluginPlugin.Log.LogInfo(
                $"[WindStorm] Storm began: speed={msg.StormSpeed}, immune netId={msg.ActivatorNetId}."
            );
        }

        public static void HandleEnd(WindStormEndMessage msg)
        {
            WindImmuneNetIds.Clear();
            // TODO: end storm VFX / audio / overlay
            IssaPluginPlugin.Log.LogInfo("[WindStorm] Storm ended.");
        }

        // ================================================================
        //  Server internals
        // ================================================================

        private IEnumerator ServerTimeoutRoutine(float duration)
        {
            yield return new WaitForSeconds(duration);

            if (SingletonNetworkBehaviour<WindManager>.HasInstance)
                SingletonNetworkBehaviour<WindManager>.Instance.NetworkcurrentWindSpeed =
                    _savedWindSpeed;

            NetworkServer.SendToAll(new WindStormEndMessage());
            _globalSessionActive = false;
            _activeInstance = null;
            _timeoutCoroutine = null;
            IssaPluginPlugin.Log.LogInfo("[WindStorm] Session ended, wind speed restored.");
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
                SingletonNetworkBehaviour<WindManager>.Instance.NetworkcurrentWindSpeed =
                    _savedWindSpeed;

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
            IssaPluginPlugin.Log.LogInfo("[WindStorm] Session ended on server stop.");
        }
    }
}
