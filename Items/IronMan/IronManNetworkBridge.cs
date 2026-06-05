using System.Collections;
using Mirror;
using UnityEngine;

namespace IssaPlugin.Items
{
    /// <summary>
    /// Attached to every player object via NetworkBridgePatches.
    ///
    /// Server responsibilities:
    ///   - Validate IronManActivateMessage (one session at a time per player).
    ///   - Run the session timer and end the session when it expires.
    ///   - Spawn rockets on IronManFireMessage.
    ///   - Receive thruster-state notifications from the client and broadcast
    ///     VFX messages to all clients (mirrors Jetpack pattern).
    ///
    /// Client responsibilities:
    ///   - Show/hide the suit prefab on remote players.
    ///   - Show/hide thruster particle effects.
    ///   - Drive IronManItem.StartSession / EndSession on the local player.
    /// </summary>
    public class IronManNetworkBridge : NetworkBridgeBase
    {
        // ── Server state ──────────────────────────────────────────────────────
        private bool      _serverSessionActive;
        private int       _serverRocketsRemaining;
        private Coroutine _serverTimerCoroutine;
        private bool      _serverThrusting;

        // ── Client state ─────────────────────────────────────────────────────
        private GameObject _suitInstance;
        private GameObject _thrusterParticles;

        // ================================================================
        //  Client → Server: activation
        // ================================================================

        public void ServerHandleActivate()
        {
            if (!isServer) return;
            if (_serverSessionActive) return;

            var inv = GetComponent<PlayerInventory>();
            if (inv == null) return;

            int slotIndex = ItemRegistry.FindSlotIndex(inv, ItemRegistry.IronManItemType);
            if (slotIndex < 0) return;

            _serverSessionActive    = true;
            _serverRocketsRemaining = ModConfig.IronMan.MaxRockets.Value;

            // Send authoritative config to the wielder so the local flight loop
            // and HUD use server values, not local defaults.
            connectionToClient?.Send(new IronManConfigMessage
            {
                Duration             = ModConfig.IronMan.Duration.Value,
                MaxRockets           = ModConfig.IronMan.MaxRockets.Value,
                FlightSpeed          = ModConfig.IronMan.FlightSpeed.Value,
                RocketExplosionScale = ModConfig.IronMan.RocketExplosionScale.Value,
            });

            connectionToClient?.Send(new IronManAmmoMessage { RocketsRemaining = _serverRocketsRemaining });

            NetworkServer.SendToAll(new IronManSuitBeginMessage { PlayerNetId = netId });

            ItemHelper.DecrementAndRemove(inv, slotIndex);

            _serverTimerCoroutine = StartCoroutine(ServerSessionTimer());
        }

        private IEnumerator ServerSessionTimer()
        {
            yield return new WaitForSeconds(ModConfig.IronMan.Duration.Value);
            ServerEndSession();
        }

        private void ServerEndSession()
        {
            if (!_serverSessionActive) return;

            _serverSessionActive = false;

            if (_serverTimerCoroutine != null)
            {
                StopCoroutine(_serverTimerCoroutine);
                _serverTimerCoroutine = null;
            }

            if (_serverThrusting)
            {
                _serverThrusting = false;
                NetworkServer.SendToAll(new IronManThrusterBroadcastEndMessage { PlayerNetId = netId });
            }

            NetworkServer.SendToAll(new IronManSuitEndMessage { PlayerNetId = netId });
        }

        // ================================================================
        //  Client → Server: thruster state (bare messages, server adds netId)
        // ================================================================

        public void ServerHandleThrusterBegin()
        {
            if (!isServer) return;
            if (!_serverSessionActive) return;
            if (_serverThrusting) return;

            _serverThrusting = true;
            NetworkServer.SendToAll(new IronManThrusterBroadcastBeginMessage { PlayerNetId = netId });
        }

        public void ServerHandleThrusterEnd()
        {
            if (!isServer) return;
            if (!_serverThrusting) return;

            _serverThrusting = false;
            NetworkServer.SendToAll(new IronManThrusterBroadcastEndMessage { PlayerNetId = netId });
        }

        // ================================================================
        //  Client → Server: fire a rocket
        // ================================================================

        public void ServerHandleFire(Vector3 aimDir)
        {
            if (!isServer) return;
            if (!_serverSessionActive) return;
            if (_serverRocketsRemaining <= 0) return;

            // Capture use index before decrement so IDs count down naturally.
            int useIndex = _serverRocketsRemaining;
            _serverRocketsRemaining--;
            connectionToClient?.Send(new IronManAmmoMessage { RocketsRemaining = _serverRocketsRemaining });

            var playerInfo  = GetComponent<PlayerInfo>();
            var spawnPos    = transform.position + Vector3.up * 1.2f + aimDir * 0.5f;
            var rotation    = Quaternion.LookRotation(aimDir);

            var rocketPrefab = GameManager.ItemSettings?.RocketPrefab;
            if (rocketPrefab != null && playerInfo != null)
            {
                var itemUseId = new ItemUseId(
                    playerInfo.PlayerId.Guid,
                    useIndex,
                    ItemType.RocketLauncher
                );

                var rocket = Object.Instantiate(rocketPrefab, spawnPos, rotation);
                rocket.gameObject.AddComponent<CustomSpawnedRocket>();
                rocket.ServerInitialize(playerInfo, null, itemUseId);
                // Explicit null connection — required by all other items to avoid
                // Mirror assigning an unexpected owner that breaks isServer on the rocket.
                NetworkServer.Spawn(rocket.gameObject, (NetworkConnectionToClient)null);

                float scale = ModConfig.IronMan.RocketExplosionScale.Value;
                if (scale != 1f)
                    ExplosionScaler.Register(rocket, scale);
            }

            NetworkServer.SendToAll(new IronManRocketFiredMessage
            {
                Origin    = spawnPos,
                Direction = aimDir,
            });

            if (_serverRocketsRemaining <= 0)
                ServerEndSession();
        }

        // ================================================================
        //  Local client notifies server of thruster state change
        // ================================================================

        public void ClientNotifyThrusterChange(bool on)
        {
            if (on)
                NetworkClient.Send(new IronManThrusterBeginMessage());
            else
                NetworkClient.Send(new IronManThrusterEndMessage());
        }

        // ================================================================
        //  Static client-side message handlers (called by NetworkManagerPatches)
        // ================================================================

        public static void HandleSuitBegin(IronManSuitBeginMessage msg)
        {
            if (!NetworkClient.spawned.TryGetValue(msg.PlayerNetId, out var identity)) return;
            identity.GetComponent<IronManNetworkBridge>()?.ClientShowSuit();
        }

        public static void HandleSuitEnd(IronManSuitEndMessage msg)
        {
            if (!NetworkClient.spawned.TryGetValue(msg.PlayerNetId, out var identity)) return;
            identity.GetComponent<IronManNetworkBridge>()?.ClientHideSuit();
        }

        public static void HandleThrusterBroadcastBegin(IronManThrusterBroadcastBeginMessage msg)
        {
            if (!NetworkClient.spawned.TryGetValue(msg.PlayerNetId, out var identity)) return;
            identity.GetComponent<IronManNetworkBridge>()?.ClientShowThrusters();
        }

        public static void HandleThrusterBroadcastEnd(IronManThrusterBroadcastEndMessage msg)
        {
            if (!NetworkClient.spawned.TryGetValue(msg.PlayerNetId, out var identity)) return;
            identity.GetComponent<IronManNetworkBridge>()?.ClientHideThrusters();
        }

        public static void HandleConfig(IronManConfigMessage msg)
        {
            var inv    = NetworkClient.localPlayer?.GetComponent<PlayerInventory>();
            var bridge = NetworkClient.localPlayer?.GetComponent<IronManNetworkBridge>();
            if (inv == null || bridge == null) return;

            IronManItem.StartSession(inv, msg);
            IssaPlugin.Overlays.IronManOverlay.OnSessionStart(msg);
        }

        public static void HandleAmmo(IronManAmmoMessage msg)
        {
            IronManItem.ApplyAmmoUpdate(msg.RocketsRemaining);
            IssaPlugin.Overlays.IronManOverlay.OnAmmoUpdate(msg.RocketsRemaining);
        }

        public static void HandleRocketFired(IronManRocketFiredMessage msg)
        {
            // Reserved for future muzzle-flash VFX at msg.Origin.
        }

        // ================================================================
        //  Per-client suit / thruster management
        // ================================================================

        private void ClientShowSuit()
        {
            ClientHideSuit();

            // For the local player the overlay handles HUD; no suit mesh is attached
            // (first-person). We still need this call to reach the local overlay
            // via HandleSuitEnd → ClientHideSuit, so we do NOT return early here.
            // Remote players get a physical suit prefab attached to their transform.
            if (!isLocalPlayer && AssetLoader.IronManSuitPrefab != null)
            {
                _suitInstance = Object.Instantiate(AssetLoader.IronManSuitPrefab);
                var rb = _suitInstance.GetComponent<Rigidbody>();
                if (rb != null) { rb.isKinematic = true; rb.useGravity = false; }
                _suitInstance.transform.SetParent(transform, false);
                _suitInstance.transform.localPosition = Vector3.zero;
                _suitInstance.transform.localRotation = Quaternion.identity;
            }
        }

        private void ClientHideSuit()
        {
            if (_suitInstance != null)
            {
                Object.Destroy(_suitInstance);
                _suitInstance = null;
            }

            // Always run session/overlay cleanup for the local player, regardless of
            // whether a suit prefab instance existed (it doesn't for first-person view).
            if (isLocalPlayer)
            {
                var inv = GetComponent<PlayerInventory>();
                if (inv != null) IronManItem.EndSession(inv);
                IssaPlugin.Overlays.IronManOverlay.OnSessionEnd();
            }
        }

        private void ClientShowThrusters()
        {
            ClientHideThrusters();

            if (AssetLoader.IronManThrusterParticlePrefab == null) return;

            _thrusterParticles = Object.Instantiate(AssetLoader.IronManThrusterParticlePrefab);
            _thrusterParticles.transform.SetParent(transform, false);
            _thrusterParticles.transform.localPosition = Vector3.zero;
            _thrusterParticles.SetActive(true);
        }

        private void ClientHideThrusters()
        {
            if (_thrusterParticles == null) return;
            Object.Destroy(_thrusterParticles);
            _thrusterParticles = null;
        }

        // ================================================================
        //  Hole cleanup
        // ================================================================

        public override void ServerHoleCleanup()
        {
            ServerEndSession();
        }

        public override void ClientHoleCleanup()
        {
            ClientHideSuit();
            ClientHideThrusters();
            if (isLocalPlayer)
            {
                IronManItem.ResetSession();
                IssaPlugin.Overlays.IronManOverlay.OnSessionEnd();
            }
        }

        public override void OnStopServer()
        {
            _serverSessionActive = false;
            _serverThrusting     = false;
            if (_serverTimerCoroutine != null)
            {
                StopCoroutine(_serverTimerCoroutine);
                _serverTimerCoroutine = null;
            }
        }
    }
}
