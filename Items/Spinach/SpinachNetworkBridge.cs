using System.Collections;
using Mirror;
using UnityEngine;

namespace IssaPlugin.Items
{
    public class SpinachNetworkBridge : NetworkBridgeBase
    {
        // ── Server state ─────────────────────────────────────────────────────
        private bool _serverSessionActive;
        private Coroutine _serverTimeout;

        // ── Client state ─────────────────────────────────────────────────────
        private GameObject _trail;
        private Coroutine _clientHideCoroutine;

        // ================================================================
        //  Client → Server
        // ================================================================

        public void ClientRequestVfx()
        {
            NetworkClient.Send(new SpinachActivateMessage());
        }

        // ================================================================
        //  Server handler — registered in NetworkManagerPatches
        // ================================================================

        /// Called by the server-side RegisterHandler when a client sends SpinachActivateMessage.
        public void ServerActivate()
        {
            if (!isServer)
                return;

            // One trail per player at a time.
            if (_serverSessionActive)
            {
                if (_serverTimeout != null)
                    StopCoroutine(_serverTimeout);
                NetworkServer.SendToAll(new SpinachTrailEndMessage { PlayerNetId = netId });
            }

            float duration = Configuration.SpinachDuration.Value;
            _serverSessionActive = true;
            NetworkServer.SendToAll(
                new SpinachTrailBeginMessage { PlayerNetId = netId, Duration = duration }
            );
            _serverTimeout = StartCoroutine(ServerEndAfter(duration));
        }

        private IEnumerator ServerEndAfter(float duration)
        {
            yield return new WaitForSeconds(duration);
            NetworkServer.SendToAll(new SpinachTrailEndMessage { PlayerNetId = netId });
            _serverSessionActive = false;
            _serverTimeout = null;
        }

        // ================================================================
        //  Client handlers — registered in NetworkManagerPatches
        // ================================================================

        public static void HandleVfxBegin(SpinachTrailBeginMessage msg)
        {
            if (!NetworkClient.spawned.TryGetValue(msg.PlayerNetId, out var identity))
                return;
            identity.GetComponent<SpinachNetworkBridge>()?.ClientShowTrail(msg.Duration);
        }

        public static void HandleVfxEnd(SpinachTrailEndMessage msg)
        {
            if (!NetworkClient.spawned.TryGetValue(msg.PlayerNetId, out var identity))
                return;
            identity.GetComponent<SpinachNetworkBridge>()?.ClientHideTrail();
        }

        // ================================================================
        //  Per-client trail management
        // ================================================================

        private void ClientShowTrail(float duration)
        {
            ClientHideTrail();

            if (AssetLoader.SpinachTrailPrefab == null)
                return;

            _trail = Object.Instantiate(AssetLoader.SpinachTrailPrefab);
            _trail.transform.SetParent(transform, false);
            _trail.transform.localPosition = Vector3.zero;
            _trail.transform.localRotation = Quaternion.identity;
            _trail.SetActive(true);

            _clientHideCoroutine = StartCoroutine(HideAfter(duration));
        }

        private void ClientHideTrail()
        {
            if (_clientHideCoroutine != null)
            {
                StopCoroutine(_clientHideCoroutine);
                _clientHideCoroutine = null;
            }
            if (_trail != null)
            {
                Object.Destroy(_trail);
                _trail = null;
            }
        }

        private IEnumerator HideAfter(float duration)
        {
            yield return new WaitForSeconds(duration);
            ClientHideTrail();
        }

        // ================================================================
        //  Hole cleanup
        // ================================================================

        public override void ServerHoleCleanup()
        {
            if (!_serverSessionActive)
                return;
            if (_serverTimeout != null)
            {
                StopCoroutine(_serverTimeout);
                _serverTimeout = null;
            }
            NetworkServer.SendToAll(new SpinachTrailEndMessage { PlayerNetId = netId });
            _serverSessionActive = false;
        }

        public override void ClientHoleCleanup() => ClientHideTrail();

        public override void OnStopServer()
        {
            if (_serverSessionActive)
            {
                if (_serverTimeout != null)
                    StopCoroutine(_serverTimeout);
                _serverTimeout = null;
                _serverSessionActive = false;
            }
        }
    }
}
