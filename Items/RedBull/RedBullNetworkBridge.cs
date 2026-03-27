using System.Collections;
using Mirror;
using UnityEngine;

namespace IssaPlugin.Items
{
    /// <summary>
    /// Attached to every player object via NetworkBridgePatches.
    ///
    /// Flow:
    ///   1. Local player uses Red Bull → OnUse calls ClientRequestTrail().
    ///   2. ClientRequestTrail sends RedBullActivateMessage to the server.
    ///   3. Server broadcasts RedBullTrailBeginMessage(netId, duration) to all clients.
    ///   4. Each client finds the player by netId and calls ClientShowTrail, which
    ///      instantiates the trail prefab locally and parents it to the player's transform.
    ///   5. After the duration the server broadcasts RedBullTrailEndMessage and every
    ///      client destroys their local copy of the trail.
    /// </summary>
    public class RedBullNetworkBridge : NetworkBridgeBase
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

        /// Called by RedBullItemDefinition.OnUse on the local player's client.
        public void ClientRequestTrail()
        {
            NetworkClient.Send(new RedBullActivateMessage());
        }

        // ================================================================
        //  Server handler — registered in NetworkManagerPatches
        // ================================================================

        /// Called by the server-side RegisterHandler when a client sends RedBullActivateMessage.
        public void ServerActivate()
        {
            if (!isServer)
                return;

            // One trail per player at a time.
            if (_serverSessionActive)
            {
                if (_serverTimeout != null)
                    StopCoroutine(_serverTimeout);
                NetworkServer.SendToAll(new RedBullTrailEndMessage { PlayerNetId = netId });
            }

            float duration = Configuration.RedBullDuration.Value;
            _serverSessionActive = true;
            NetworkServer.SendToAll(
                new RedBullTrailBeginMessage { PlayerNetId = netId, Duration = duration }
            );
            _serverTimeout = StartCoroutine(ServerEndAfter(duration));
        }

        private IEnumerator ServerEndAfter(float duration)
        {
            yield return new WaitForSeconds(duration);
            NetworkServer.SendToAll(new RedBullTrailEndMessage { PlayerNetId = netId });
            _serverSessionActive = false;
            _serverTimeout = null;
        }

        // ================================================================
        //  Client handlers — registered in NetworkManagerPatches
        // ================================================================

        public static void HandleTrailBegin(RedBullTrailBeginMessage msg)
        {
            if (!NetworkClient.spawned.TryGetValue(msg.PlayerNetId, out var identity))
                return;
            identity.GetComponent<RedBullNetworkBridge>()?.ClientShowTrail(msg.Duration);
        }

        public static void HandleTrailEnd(RedBullTrailEndMessage msg)
        {
            if (!NetworkClient.spawned.TryGetValue(msg.PlayerNetId, out var identity))
                return;
            identity.GetComponent<RedBullNetworkBridge>()?.ClientHideTrail();
        }

        // ================================================================
        //  Per-client trail management
        // ================================================================

        private void ClientShowTrail(float duration)
        {
            ClientHideTrail();

            if (AssetLoader.RedBullTrailPrefab == null)
                return;

            _trail = Object.Instantiate(AssetLoader.RedBullTrailPrefab);
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
            NetworkServer.SendToAll(new RedBullTrailEndMessage { PlayerNetId = netId });
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
