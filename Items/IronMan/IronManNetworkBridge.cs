using System.Collections;
using System.Reflection;
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
    ///   - Broadcast suit and thruster VFX messages to all clients.
    ///
    /// Client responsibilities:
    ///   - Show/hide the suit prefab on the owning player.
    ///   - Show/hide thruster particle effects.
    ///   - Drive IronManItem.StartSession / EndSession on the local player.
    /// </summary>
    public class IronManNetworkBridge : NetworkBridgeBase
    {
        private static readonly MethodInfo ServerExplodeMethod = typeof(Rocket).GetMethod(
            "ServerExplode",
            BindingFlags.NonPublic | BindingFlags.Instance
        );

        // ── Server state ──────────────────────────────────────────────────────
        private bool      _serverSessionActive;
        private int       _serverRocketsRemaining;
        private Coroutine _serverTimerCoroutine;

        // ── Client state ─────────────────────────────────────────────────────
        private GameObject _suitInstance;
        private GameObject _thrusterParticles;
        private bool       _clientThrusterOn;

        // ================================================================
        //  Client → Server: activation
        // ================================================================

        public void ServerHandleActivate()
        {
            if (!isServer) return;
            if (_serverSessionActive) return;

            var inv = GetComponent<PlayerInventory>();
            if (inv == null) return;

            // Consume the item slot.
            int slotIndex = ItemRegistry.FindSlotIndex(inv, ItemRegistry.IronManItemType);
            if (slotIndex < 0) return;

            _serverSessionActive    = true;
            _serverRocketsRemaining = ModConfig.IronMan.MaxRockets.Value;

            // Send config to the wielder only.
            connectionToClient?.Send(new IronManConfigMessage
            {
                Duration             = ModConfig.IronMan.Duration.Value,
                MaxRockets           = ModConfig.IronMan.MaxRockets.Value,
                FlightSpeed          = ModConfig.IronMan.FlightSpeed.Value,
                RocketExplosionScale = ModConfig.IronMan.RocketExplosionScale.Value,
            });

            // Send ammo update to wielder.
            connectionToClient?.Send(new IronManAmmoMessage { RocketsRemaining = _serverRocketsRemaining });

            // Tell all clients to show the suit.
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

            NetworkServer.SendToAll(new IronManSuitEndMessage { PlayerNetId = netId });
        }

        // ================================================================
        //  Client → Server: flight input (authorise thruster VFX only)
        // ================================================================

        public void ServerHandleFlightInput(Vector3 moveDir)
        {
            if (!isServer) return;
            if (!_serverSessionActive) return;
            // No server-side movement — client applies force locally. Server only
            // uses this to confirm the player is actively thrusting (VFX is handled
            // via ClientNotifyThrusterChange to avoid double-broadcasting).
        }

        // ================================================================
        //  Client → Server: fire a rocket
        // ================================================================

        public void ServerHandleFire(Vector3 aimDir)
        {
            if (!isServer) return;
            if (!_serverSessionActive) return;
            if (_serverRocketsRemaining <= 0) return;

            _serverRocketsRemaining--;
            connectionToClient?.Send(new IronManAmmoMessage { RocketsRemaining = _serverRocketsRemaining });

            // Spawn the rocket from slightly in front of the player.
            var playerInfo = GetComponent<PlayerInfo>();
            var spawnPos   = transform.position + Vector3.up * 1.2f + aimDir * 0.5f;
            var rotation   = Quaternion.LookRotation(aimDir);

            var rocketPrefab = GameManager.ItemSettings?.RocketPrefab;
            if (rocketPrefab != null)
            {
                var rocket = Object.Instantiate(rocketPrefab, spawnPos, rotation);
                rocket.gameObject.AddComponent<CustomSpawnedRocket>();
                if (playerInfo != null)
                    rocket.ServerInitialize(playerInfo, null, 0);
                NetworkServer.Spawn(rocket.gameObject);

                float scale = ModConfig.IronMan.RocketExplosionScale.Value;
                if (scale != 1f)
                    ExplosionScaler.Register(rocket, scale);
            }

            // Broadcast VFX to all clients.
            NetworkServer.SendToAll(new IronManRocketFiredMessage
            {
                Origin    = spawnPos,
                Direction = aimDir,
            });

            if (_serverRocketsRemaining <= 0)
                ServerEndSession();
        }

        // ================================================================
        //  Client notifies of thruster state change
        // ================================================================

        public void ClientNotifyThrusterChange(bool on)
        {
            if (on)
                NetworkClient.Send(new IronManThrusterBeginMessage { PlayerNetId = netId });
            else
                NetworkClient.Send(new IronManThrusterEndMessage { PlayerNetId = netId });
        }

        // ── Server handlers called by NetworkManagerPatches ────────────────────

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

        public static void HandleThrusterBegin(IronManThrusterBeginMessage msg)
        {
            if (!NetworkClient.spawned.TryGetValue(msg.PlayerNetId, out var identity)) return;
            identity.GetComponent<IronManNetworkBridge>()?.ClientShowThrusters();
        }

        public static void HandleThrusterEnd(IronManThrusterEndMessage msg)
        {
            if (!NetworkClient.spawned.TryGetValue(msg.PlayerNetId, out var identity)) return;
            identity.GetComponent<IronManNetworkBridge>()?.ClientHideThrusters();
        }

        public static void HandleConfig(IronManConfigMessage msg)
        {
            // Only the owning client receives this; start the local session.
            var inv = NetworkClient.localPlayer?.GetComponent<PlayerInventory>();
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
            // VFX only — future: spawn a muzzle flash at msg.Origin.
        }

        // ================================================================
        //  Per-client suit / thruster management
        // ================================================================

        private void ClientShowSuit()
        {
            ClientHideSuit();

            if (isLocalPlayer)
            {
                // Local player: flight loop is driven by IronManItem; no suit mesh needed
                // (first-person view). The overlay handles HUD.
                return;
            }

            if (AssetLoader.IronManSuitPrefab == null) return;

            _suitInstance = Object.Instantiate(AssetLoader.IronManSuitPrefab);
            var rb = _suitInstance.GetComponent<Rigidbody>();
            if (rb != null) { rb.isKinematic = true; rb.useGravity = false; }
            _suitInstance.transform.SetParent(transform, false);
            _suitInstance.transform.localPosition = Vector3.zero;
            _suitInstance.transform.localRotation = Quaternion.identity;
        }

        private void ClientHideSuit()
        {
            if (_suitInstance == null) return;
            Object.Destroy(_suitInstance);
            _suitInstance = null;

            if (isLocalPlayer)
            {
                var inv = GetComponent<PlayerInventory>();
                if (inv != null) IronManItem.EndSession(inv);
                IssaPlugin.Overlays.IronManOverlay.OnSessionEnd();
            }
        }

        private void ClientShowThrusters()
        {
            if (_clientThrusterOn) return;
            _clientThrusterOn = true;
            ClientHideThrusters(); // destroy any stale instance

            if (AssetLoader.IronManThrusterParticlePrefab == null) return;

            _thrusterParticles = Object.Instantiate(AssetLoader.IronManThrusterParticlePrefab);
            _thrusterParticles.transform.SetParent(transform, false);
            _thrusterParticles.transform.localPosition = Vector3.zero;
            _thrusterParticles.SetActive(true);
        }

        private void ClientHideThrusters()
        {
            _clientThrusterOn = false;
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
            if (_serverTimerCoroutine != null)
            {
                StopCoroutine(_serverTimerCoroutine);
                _serverTimerCoroutine = null;
            }
        }
    }
}
