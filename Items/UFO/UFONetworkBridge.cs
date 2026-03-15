using System.Collections;
using Mirror;
using UnityEngine;
using UnityEngine.InputSystem;

namespace IssaPlugin.Items
{
    /// <summary>
    /// Attached to every player object via NetworkBridgePatches.
    ///
    /// SERVER SIDE: Validates activation, spawns the UFO prefab, drives
    /// terrain-following movement from client input, and fires the orbital
    /// laser after an anticipation delay.
    ///
    /// CLIENT SIDE (owning player only): Manages the orbit camera, reads
    /// WASD + mouse input each frame, sends UFOMoveMessage to the server,
    /// and fires UFOFireLaserMessage on left-click.
    ///
    /// Multiple players may run UFO sessions simultaneously — there is no
    /// global session lock.
    /// </summary>
    public class UFONetworkBridge : NetworkBehaviour
    {
        // ================================================================
        //  Global server lock  (server only, static)
        // ================================================================

        private static bool _globalSessionActive;
        private static UFONetworkBridge _activeSessionBridge;

        /// Server-side reference to the active UFO GameObject.
        public static GameObject ActiveUFO => _activeSessionBridge?._serverUFO;

        // Specific Vector3
        public static Vector3 UFOLaserTargetVector = new Vector3(0xABCDEF0, 0xABCDEF0, 0xABCDEF0);

        // ================================================================
        //  Server-side per-instance state
        // ================================================================

        private bool _serverSessionActive;
        private GameObject _serverUFO;
        private int _laserUsesRemaining;
        private int _laserUseIndex;
        private bool _laserPending;
        // private Vector3 _pendingLaserGroundPos;
        private float _lastLaserTime;
        private Coroutine _serverTimeout;

        // ================================================================
        //  Client-side per-instance state (owning client only)
        // ================================================================

        public bool LocalSessionActive { get; private set; }
        private bool _forceEnd;

        private bool _shotDown;

        // ================================================================
        //  Mirror lifecycle
        // ================================================================

        public override void OnStopServer()
        {
            if (_serverSessionActive)
            {
                IssaPluginPlugin.Log.LogInfo("[UFO] Player disconnected during session — cleanup.");
                ForceServerCleanup();
            }
        }

        // ================================================================
        //  Client → Server  (called by UFOMessageHandlers in NetworkManagerPatches)
        // ================================================================

        public void ServerStartUFO()
        {
            if (_serverSessionActive)
            {
                IssaPluginPlugin.Log.LogWarning("[UFO] Session already active for this player.");
                return;
            }

            if (_globalSessionActive)
            {
                IssaPluginPlugin.Log.LogWarning("[UFO] Another player's UFO is already active.");
                connectionToClient.Send(new UFOBusyMessage());
                return;
            }

            var inventory = GetComponent<PlayerInventory>();
            if (inventory == null)
                return;

            var equipped = inventory.GetEffectivelyEquippedItem(true);
            if (equipped != UFOItem.UFOItemType)
            {
                IssaPluginPlugin.Log.LogWarning("[UFO] Player does not have UFO item equipped.");
                return;
            }

            if (AssetLoader.UFOPrefab == null)
            {
                IssaPluginPlugin.Log.LogError("[UFO] UFO prefab not loaded.");
                return;
            }

            ItemHelper.ConsumeEquippedItem(inventory);

            // Spawn UFO directly above the player; UFOFlyBehaviour terrain-follow
            // takes over immediately and settles at the configured altitude.
            Vector3 spawnPos =
                inventory.PlayerInfo.transform.position
                + Vector3.up * Configuration.UFOAltitude.Value;

            var ufoGo = Object.Instantiate(AssetLoader.UFOPrefab, spawnPos, Quaternion.identity);

            var flyBehaviour = ufoGo.AddComponent<UFOFlyBehaviour>();
            var hitReceiver = ufoGo.AddComponent<UFOHitReceiver>();

            // Enable the Rigidbody for physics-driven movement (prefab may ship kinematic).
            var rb = ufoGo.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.isKinematic = false;
                rb.useGravity = false;
            }

            // Wire up rocket-hit threshold callback.
            hitReceiver.OnHitsExceeded = () =>
            {
                ServerUFOShotDown();
            };

            NetworkServer.Spawn(ufoGo);

            var ni = ufoGo.GetComponent<NetworkIdentity>();

            _serverUFO = ufoGo;
            _serverSessionActive = true;
            _globalSessionActive = true;
            _activeSessionBridge = this;

            _laserUsesRemaining = (int)Configuration.UFOLaserUses.Value;
            _laserUseIndex = 0;
            _laserPending = false;
            _lastLaserTime = -999f;

            connectionToClient.Send(new UFOBeginClientMessage { UFONetId = ni.netId });

            _serverTimeout = StartCoroutine(ServerTimeoutRoutine());

            IssaPluginPlugin.Log.LogInfo("[UFO] Server session started.");
        }

        public void ServerEndUFO()
        {
            EndServerSession();
        }

        /// Called every frame from UFOMoveMessage while the client is in a session.
        public void ServerMoveUFO(Vector3 worldMoveDir)
        {
            if (!_serverSessionActive || _serverUFO == null)
                return;

            var fly = _serverUFO.GetComponent<UFOFlyBehaviour>();
            if (fly != null)
                fly.MoveInput = worldMoveDir;
        }

        public void ServerFireLaser()
        {
            if (!_serverSessionActive || _serverUFO == null)
                return;

            if (_laserPending)
                return;

            if (_laserUsesRemaining <= 0)
                return;

            if (Time.time - _lastLaserTime < Configuration.UFOLaserCooldown.Value)
                return;

            _lastLaserTime = Time.time;
            LaserAnticipationCoroutine();
        }

        public void ServerUFOShotDown()
        {
            IssaPluginPlugin.Log.LogInfo("[UFO] Server UFO Shot Down sequence started.");

            NetworkServer.SendToAll(new UFOShotDownMessage { });

            foreach (var col in _serverUFO.GetComponents<Collider>())
                col.enabled = false;

            var rb = _serverUFO.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.isKinematic = false;
                rb.useGravity = true;
                rb.AddForce(Vector3.down * Configuration.UFOCrashDownwardForce.Value);
            }

            var crashBehaviour = _serverUFO.AddComponent<UFOCrashBehaviour>();
            crashBehaviour.Rigidbody = rb;
            crashBehaviour.UFONetworkBridge = this;
        }

        // ================================================================
        //  Server → Client  (called by message handlers registered in
        //  NetworkManagerPatches)
        // ================================================================

        public void ClientBeginUFO(uint ufoNetId)
        {
            StartCoroutine(RunLocalSession(ufoNetId));
        }

        public void ClientEndUFO(bool shotDown)
        {
            _forceEnd = true;
            _shotDown = shotDown;
        }

        public void ClientUFOBusy()
        {
            IssaPluginPlugin.Log.LogInfo("[UFO] UFO is already in use by another player.");
        }

        // ================================================================
        //  Server internals
        // ================================================================

        private void LaserAnticipationCoroutine()
        {
            _laserPending = true;

            // Fire at wherever the UFO ended up.
            if (_serverSessionActive && _serverUFO != null)
            {
                var inventory = GetComponent<PlayerInventory>();
                if (inventory != null)
                {
                    var itemUseId = new ItemUseId(
                        inventory.PlayerInfo.PlayerId.Guid,
                        ++_laserUseIndex,
                        ItemType.OrbitalLaser
                    );
                    OrbitalLaserManager.ServerActivateLaser(
                        null,
                        UFOLaserTargetVector,
                        inventory,
                        itemUseId
                    );
                    _laserUsesRemaining--;
                    // IssaPluginPlugin.Log.LogInfo(
                    //     $"[UFO] Laser fired at {_pendingLaserGroundPos}. "
                    //         + $"Remaining: {_laserUsesRemaining}"
                    // );
                }
            }

            _laserPending = false;
        }

        private void EndServerSession()
        {
            if (!_serverSessionActive)
                return;

            if (_serverTimeout != null)
            {
                StopCoroutine(_serverTimeout);
                _serverTimeout = null;
            }

            _serverSessionActive = false;
            connectionToClient.Send(new UFOEndClientMessage());

            if (_serverUFO != null)
            {
                NetworkServer.Destroy(_serverUFO);
                _serverUFO = null;
            }

            IssaPluginPlugin.Log.LogInfo("[UFO] Server session ended.");
        }

        private void ForceServerCleanup()
        {
            if (_serverTimeout != null)
            {
                StopCoroutine(_serverTimeout);
                _serverTimeout = null;
            }

            if (_serverUFO != null)
            {
                NetworkServer.Destroy(_serverUFO);
                _serverUFO = null;
            }

            _serverSessionActive = false;
        }

        private IEnumerator ServerTimeoutRoutine()
        {
            yield return new WaitForSeconds(Configuration.UFODuration.Value + 10f);
            if (_serverSessionActive)
                EndServerSession();
        }

        // ================================================================
        //  Client: local session coroutine
        // ================================================================

        private IEnumerator RunLocalSession(uint ufoNetId)
        {
            LocalSessionActive = true;
            _forceEnd = false;

            InputManager.Controls.Gameplay.Disable();

            // Skip one frame so wasPressedThisFrame on the activation key
            // doesn't immediately read as an Escape / early-exit press.
            yield return null;

            // Wait for Mirror to spawn the UFO on this client.
            float waited = 0f;
            NetworkIdentity ufoIdentity = null;
            while (!NetworkClient.spawned.TryGetValue(ufoNetId, out ufoIdentity) && waited < 2f)
            {
                waited += Time.deltaTime;
                yield return null;
            }

            if (ufoIdentity == null)
            {
                IssaPluginPlugin.Log.LogError("[UFO] UFO not found in spawned dict after wait.");
                UFOOverlay.SetActive(false, 0);
                LocalSessionActive = false;
                InputManager.Controls.Gameplay.Enable();
                yield break;
            }

            Transform ufoTransform = ufoIdentity.transform;

            // ── Orbit camera setup ────────────────────────────────────────────
            CameraModuleController.TryGetOrbitModule(out var orbitModule);
            float savedPitch = orbitModule?.Pitch ?? 0f;
            float savedYaw = orbitModule?.Yaw ?? 0f;
            bool savedDisablePhysics = orbitModule?.disablePhysics ?? false;

            float cameraYaw = savedYaw;

            if (orbitModule != null)
            {
                orbitModule.SetSubject(ufoTransform);
                orbitModule.SetPitch(Configuration.UFOCameraPitch.Value);
                orbitModule.SetDistanceAddition(Configuration.UFOCameraDistance.Value);
                orbitModule.disablePhysics = true;
                orbitModule.ForceUpdateModule();
            }

            int localLaserUsesRemaining = (int)Configuration.UFOLaserUses.Value;
            float sessionElapsed = 0f;
            float sessionDuration = Configuration.UFODuration.Value;

            UFOOverlay.SetActive(true, localLaserUsesRemaining);

            // ── Main control loop ─────────────────────────────────────────────
            while (!_forceEnd && sessionElapsed < sessionDuration)
            {
                sessionElapsed += Time.deltaTime;

                var mouse = Mouse.current;
                var keyboard = Keyboard.current;

                // Mouse X → rotate orbit camera yaw.
                if (mouse != null)
                {
                    float mouseX =
                        mouse.delta.x.ReadValue() * Configuration.UFOMouseSensitivity.Value;
                    cameraYaw += mouseX;
                    if (cameraYaw >= 360f)
                        cameraYaw -= 360f;
                    if (cameraYaw < 0f)
                        cameraYaw += 360f;
                    orbitModule?.SetYaw(cameraYaw);
                }

                // Keep camera snapped to the UFO without lerp lag.
                orbitModule?.ForceUpdateModule();

                // WASD → camera-relative world-space move direction.
                float fwd = 0f,
                    strafe = 0f;
                if (keyboard != null)
                {
                    if (keyboard[Key.W].isPressed)
                        fwd += 1f;
                    if (keyboard[Key.S].isPressed)
                        fwd -= 1f;
                    if (keyboard[Key.A].isPressed)
                        strafe -= 1f;
                    if (keyboard[Key.D].isPressed)
                        strafe += 1f;

                    if (keyboard[Key.Escape].wasPressedThisFrame)
                        _forceEnd = true;
                }

                Vector3 worldMoveDir = Vector3.zero;
                if (Mathf.Abs(fwd) > 0.001f || Mathf.Abs(strafe) > 0.001f)
                {
                    Quaternion camYawRot = Quaternion.Euler(0f, cameraYaw, 0f);
                    worldMoveDir =
                        camYawRot * Vector3.forward * fwd + camYawRot * Vector3.right * strafe;
                    if (worldMoveDir.sqrMagnitude > 1f)
                        worldMoveDir.Normalize();
                }

                NetworkClient.Send(new UFOMoveMessage { WorldMoveDir = worldMoveDir });

                // Left click → fire laser.
                if (
                    mouse != null
                    && mouse.leftButton.wasPressedThisFrame
                    && localLaserUsesRemaining > 0
                )
                {
                    NetworkClient.Send(new UFOFireLaserMessage());
                    localLaserUsesRemaining--;
                    UFOOverlay.UpdateLaserUses(localLaserUsesRemaining);
                }

                UFOOverlay.UpdateTimeRemaining(sessionDuration - sessionElapsed);

                yield return null;
            }

            // ── Session end ───────────────────────────────────────────────────
            // Stop the UFO before the server destroys it.
            // If we were shot down, then the server will handle things.
            if (!_shotDown)
            {
                NetworkClient.Send(new UFOMoveMessage { WorldMoveDir = Vector3.zero });
                NetworkClient.Send(new UFOEndMessage());
            }

            UFOOverlay.SetActive(false, 0);

            // Restore orbit camera to normal player tracking.
            if (orbitModule != null)
            {
                orbitModule.SetDistanceAddition(0f);
                orbitModule.disablePhysics = savedDisablePhysics;

                var playerMovement = GameManager.LocalPlayerMovement;
                if (playerMovement != null)
                    orbitModule.SetSubject(playerMovement.transform);

                orbitModule.SetPitch(savedPitch);
                orbitModule.SetYaw(savedYaw);
                orbitModule.ForceUpdateModule();
            }

            InputManager.Controls.Gameplay.Enable();
            LocalSessionActive = false;

            IssaPluginPlugin.Log.LogInfo("[UFO] Client session ended.");
        }
    }
}
