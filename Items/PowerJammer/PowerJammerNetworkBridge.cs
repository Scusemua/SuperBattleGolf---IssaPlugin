using System.Collections;
using IssaPlugin.Overlays;
using Mirror;
using UnityEngine;

namespace IssaPlugin.Items
{
    /// <summary>
    /// Server-authoritative lifetime for the global Power Jammer session.
    /// The gameplay effect itself is client-local because it only changes HUD rendering.
    /// </summary>
    public class PowerJammerNetworkBridge : NetworkBridgeBase
    {
        private static bool _globalSessionActive;
        private static Coroutine _timeoutCoroutine;
        private static PowerJammerNetworkBridge _activeInstance;
        private static float _serverEndTime;
        private static uint _activatorNetId;
        private static bool _affectsActivator;

        public void ServerActivatePowerJammer()
        {
            if (_globalSessionActive)
            {
                IssaPluginPlugin.Log.LogWarning(
                    "[PowerJammer] A Power Jammer session is already active."
                );
                return;
            }

            var inventory = CachedInventory;
            if (inventory == null)
                return;

            if (inventory.GetEffectivelyEquippedItem(true) != ItemRegistry.PowerJammerItemType)
            {
                IssaPluginPlugin.Log.LogWarning(
                    "[PowerJammer] Player does not have Power Jammer equipped."
                );
                return;
            }

            ItemHelper.ConsumeEquippedItem(inventory);

            float duration = ModConfig.PowerJammer.Duration.Value;
            _globalSessionActive = true;
            _activeInstance = this;
            _serverEndTime = Time.time + duration;
            _activatorNetId = netId;
            _affectsActivator = ModConfig.PowerJammer.AffectsUser.Value;

            NetworkServer.SendToAll(CreateBeginMessage(duration));
            _timeoutCoroutine = StartCoroutine(ServerTimeoutRoutine(duration));

            IssaPluginPlugin.Log.LogInfo(
                $"[PowerJammer] Server session started for {duration:F1}s "
                    + $"(affects activator={_affectsActivator})."
            );
        }

        public static void HandlePowerJammerBegin(PowerJammerBeginMessage msg)
        {
            bool isActivator =
                NetworkClient.localPlayer != null
                && NetworkClient.localPlayer.netId == msg.ActivatorNetId;
            bool exempt = isActivator && !msg.AffectsActivator;

            PowerJammerItem.IsAffected = !exempt;
            PowerJammerOverlay.Instance?.SetActive(
                active: true,
                duration: msg.Duration,
                locallyImmune: exempt
            );

            // Remove an already-rendered prediction immediately. The Harmony patch
            // keeps subsequent trajectory updates from drawing it again.
            if (!exempt)
                SwingPowerBarUi.HideTerrainLayers();

            IssaPluginPlugin.Log.LogInfo(
                exempt
                    ? "[PowerJammer] Client session started (activator is immune)."
                    : "[PowerJammer] Client session started."
            );
        }

        public static void HandlePowerJammerEnd(PowerJammerEndMessage msg)
        {
            ClearClientState();
            IssaPluginPlugin.Log.LogInfo("[PowerJammer] Client session ended.");
        }

        /// <summary>
        /// Gives a client joining mid-effect the authoritative remaining duration.
        /// </summary>
        public static void SyncActiveSessionToConnection(NetworkConnectionToClient connection)
        {
            if (!_globalSessionActive || connection == null)
                return;

            float remaining = Mathf.Max(0f, _serverEndTime - Time.time);
            if (remaining > 0f)
                connection.Send(CreateBeginMessage(remaining));
        }

        private static PowerJammerBeginMessage CreateBeginMessage(float duration) =>
            new PowerJammerBeginMessage
            {
                Duration = duration,
                ActivatorNetId = _activatorNetId,
                AffectsActivator = _affectsActivator,
            };

        private IEnumerator ServerTimeoutRoutine(float duration)
        {
            yield return new WaitForSeconds(duration);
            EndServerSession();
            IssaPluginPlugin.Log.LogInfo("[PowerJammer] Server session ended.");
        }

        private static void EndServerSession()
        {
            if (!_globalSessionActive)
                return;

            NetworkServer.SendToAll(new PowerJammerEndMessage());
            _globalSessionActive = false;
            _timeoutCoroutine = null;
            _activeInstance = null;
            _serverEndTime = 0f;
            _activatorNetId = 0;
            _affectsActivator = false;
        }

        private static void ClearClientState()
        {
            PowerJammerItem.IsAffected = false;
            PowerJammerOverlay.Instance?.SetActive(false);
        }

        public static void GlobalServerHoleCleanup()
        {
            if (!_globalSessionActive)
                return;

            if (_timeoutCoroutine != null && _activeInstance != null)
                _activeInstance.StopCoroutine(_timeoutCoroutine);
            EndServerSession();
            IssaPluginPlugin.Log.LogInfo(
                "[PowerJammer] Session force-ended for hole transition."
            );
        }

        public override void ServerHoleCleanup() => GlobalServerHoleCleanup();

        public override void ClientHoleCleanup() => ClearClientState();

        public override void OnStopClient()
        {
            // Every player owns this bridge. A remote player despawning must not
            // clear the local effect; only local-player teardown/full disconnect should.
            if (isLocalPlayer || !NetworkClient.active)
                ClearClientState();
            base.OnStopClient();
        }

        public override void OnStopServer()
        {
            // A bridge stops whenever its player leaves. Only the activator's bridge
            // owns the coroutine/session; unrelated disconnects must not end the effect.
            if (!_globalSessionActive || !ReferenceEquals(this, _activeInstance))
                return;

            if (_timeoutCoroutine != null && _activeInstance != null)
                _activeInstance.StopCoroutine(_timeoutCoroutine);
            EndServerSession();
            IssaPluginPlugin.Log.LogInfo(
                "[PowerJammer] Session ended and global lock released on server stop."
            );
        }
    }
}
