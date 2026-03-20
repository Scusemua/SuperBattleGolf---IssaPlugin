using System.Collections;
using IssaPlugin.Overlays;
using Mirror;
using UnityEngine;
using UnityEngine.InputSystem;

namespace IssaPlugin.Items
{
    /// <summary>
    /// Attached to every player object via NetworkBridgePatches.
    ///
    /// SERVER SIDE: Validates activation, spawns the Donut prefab, drives
    /// terrain-following movement from client input, and fires the orbital
    /// laser after an anticipation delay.
    ///
    /// CLIENT SIDE (owning player only): Manages the orbit camera, reads
    /// WASD + mouse input each frame, sends DonutMoveMessage to the server,
    /// and fires DonutFireLaserMessage on left-click.
    ///
    /// Multiple players may run Donut sessions simultaneously — there is no
    /// global session lock.
    /// </summary>
    public class DonutNetworkBridge : NetworkBehaviour
    {
        // ================================================================
        //  Global server lock  (server only, static)
        // ================================================================

        private static bool _globalSessionActive;
        private static DonutNetworkBridge _activeSessionBridge;

        // Transform whose position is repeatedly set to the raycast
        // to the ground by the Donut.
        private static Transform donutLaserTarget = null;

        /// Server-side reference to the active Donut GameObject.
        public static GameObject ActiveDonut => _activeSessionBridge?._serverDonut;

        public static Transform DonutLaserTarget
        {
            get => donutLaserTarget;
            set { donutLaserTarget = value; }
        }

        // Vector3 passed to OrbitalLaserManager.ServerActivateLaser so when we execute
        // our patched version, we can tell that the laser came from the Donut and can
        // adjust the target appropriately.
        public static readonly Vector3 DonutLaserTargetVector = new Vector3(
            0xABCDEF0,
            0xABCDEF0,
            0xABCDEF0
        );

        // ================================================================
        //  Server-side per-instance state
        // ================================================================

        private bool _serverSessionActive;
        private GameObject _serverDonut;
        private int _laserUsesRemaining;
        private int _laserUseIndex;
        private bool _laserPending;

        // private Vector3 _pendingLaserGroundPos;
        private float _lastLaserTime;
        private Coroutine _serverTimeout;

        public bool PendingDonutHoming;

        // ================================================================
        //  Client-side per-instance state (owning client only)
        // ================================================================

        public bool LocalSessionActive { get; private set; }
        private bool _forceEnd;

        private bool _shotDown;

        private int localLaserUsesRemaining;
        private float _localLastLaserTime;

        private bool _canMove; // Can't move while shooting

        // Rate-limiting for DonutMoveMessage: send at most 20 Hz and only when changed.
        private const float InputSendInterval = 0.05f;
        private Vector3 _lastSentMoveDir;
        private float _moveSendTimer;

        // ================================================================
        //  Mirror lifecycle
        // ================================================================

        public override void OnStopServer()
        {
            if (_serverSessionActive)
            {
                IssaPluginPlugin.Log.LogInfo(
                    "[Donut] Player disconnected during session — cleanup."
                );
                ForceServerCleanup();
            }
        }

        // ================================================================
        //  Client → Server  (called by DonutMessageHandlers in NetworkManagerPatches)
        // ================================================================

        public void ServerStartDonut()
        {
            if (_serverSessionActive)
            {
                IssaPluginPlugin.Log.LogWarning("[Donut] Session already active for this player.");
                return;
            }

            if (_globalSessionActive)
            {
                IssaPluginPlugin.Log.LogWarning(
                    "[Donut] Another player's Donut is already active."
                );
                connectionToClient.Send(new DonutBusyMessage());
                return;
            }

            var inventory = GetComponent<PlayerInventory>();
            if (inventory == null)
                return;

            var equipped = inventory.GetEffectivelyEquippedItem(true);
            if (equipped != DonutItem.DonutItemType)
            {
                IssaPluginPlugin.Log.LogWarning(
                    "[Donut] Player does not have Donut item equipped."
                );
                return;
            }

            if (AssetLoader.DonutPrefab == null)
            {
                IssaPluginPlugin.Log.LogError("[Donut] Donut prefab not loaded.");
                return;
            }

            ItemHelper.ConsumeEquippedItem(inventory);

            // Spawn Donut directly above the player; DonutFlyBehaviour terrain-follow
            // takes over immediately and settles at the configured altitude.
            Vector3 spawnPos =
                inventory.PlayerInfo.transform.position
                + Vector3.up * Configuration.DonutAltitude.Value;

            var donutGo = Object.Instantiate(
                AssetLoader.DonutPrefab,
                spawnPos,
                Quaternion.identity
            );

            var flyBehaviour = donutGo.AddComponent<DonutFlyBehaviour>();
            var hitReceiver = donutGo.AddComponent<DonutHitReceiver>();

            // Enable the Rigidbody for physics-driven movement (prefab may ship kinematic).
            var rb = donutGo.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.isKinematic = false;
                rb.useGravity = false;
            }

            // Wire up rocket-hit threshold callback.
            hitReceiver.OnHitsExceeded = () =>
            {
                ServerDonutShotDown();
            };

            NetworkServer.Spawn(donutGo);

            var ni = donutGo.GetComponent<NetworkIdentity>();

            _serverDonut = donutGo;
            _serverSessionActive = true;
            _globalSessionActive = true;
            _activeSessionBridge = this;

            _laserUsesRemaining = (int)Configuration.DonutLaserUses.Value;
            _laserUseIndex = 0;
            _laserPending = false;
            _lastLaserTime = -999f;

            connectionToClient.Send(new DonutBeginClientMessage { DonutNetId = ni.netId });

            _serverTimeout = StartCoroutine(ServerTimeoutRoutine());

            IssaPluginPlugin.Log.LogInfo("[Donut] Server session started.");
        }

        public void ServerEndDonut(bool shouldCrash)
        {
            if (!_serverSessionActive)
                return;
            if (!shouldCrash)
                EndServerSession();
            else
                ServerTriggerCrashBehaviour();
        }

        /// Called when the client is locked onto the Donut and fires a rocket.
        /// Sets PendingDonutHoming so the next rocket homes toward the Donut.
        public void ServerPrepareDonutRocket()
        {
            if (ActiveDonut == null)
                return;
            PendingDonutHoming = true;
        }

        /// Called every frame from DonutMoveMessage while the client is in a session.
        public void ServerMoveDonut(Vector3 worldMoveDir)
        {
            if (!_serverSessionActive || _serverDonut == null)
                return;

            var fly = _serverDonut.GetComponent<DonutFlyBehaviour>();
            if (fly != null)
                fly.MoveInput = worldMoveDir;
        }

        public void ServerFireLaser()
        {
            if (!_serverSessionActive || _serverDonut == null)
                return;

            if (_laserPending)
                return;

            if (_laserUsesRemaining <= 0)
                return;

            if (Time.time - _lastLaserTime < Configuration.DonutLaserCooldown.Value)
                return;

            _lastLaserTime = Time.time;
            LaserAnticipationCoroutine();
        }

        public void ServerDonutShotDown()
        {
            IssaPluginPlugin.Log.LogInfo("[Donut] Server Donut Shot Down sequence started.");

            NetworkServer.SendToAll(new DonutShotDownMessage { });

            ServerTriggerCrashBehaviour();
        }

        public void ServerTriggerCrashBehaviour()
        {
            foreach (var col in _serverDonut.GetComponents<Collider>())
                col.enabled = true;

            var donutFlyBehaviour = _serverDonut.GetComponent<DonutFlyBehaviour>();
            if (donutFlyBehaviour != null)
                donutFlyBehaviour.enabled = false;

            var rb = _serverDonut.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.isKinematic = false;
                rb.useGravity = true;
            }

            var crashBehaviour = _serverDonut.AddComponent<DonutCrashBehaviour>();
            crashBehaviour.Rigidbody = rb;
            crashBehaviour.DonutNetworkBridge = this;
        }

        public void ServerSpawnImpactRocket(Vector3 position)
        {
            var inventory = GetComponent<PlayerInventory>();
            if (inventory == null)
                return;

            var rocket = Object.Instantiate(
                GameManager.ItemSettings.RocketPrefab,
                position,
                Quaternion.identity
            );

            if (rocket == null) {
                IssaPluginPlugin.Log.LogError("[Donut] Rocket prefab failed to instantiate.");
                return;
            }

            // Prevent this rocket from being assigned homing behaviour.
            rocket.gameObject.AddComponent<CustomSpawnedRocket>();

            var itemUseId = new ItemUseId(
                inventory.PlayerInfo.PlayerId.Guid,
                int.MaxValue,
                ItemType.RocketLauncher
            );

            rocket.ServerInitialize(inventory.PlayerInfo, null, itemUseId);
            NetworkServer.Spawn(rocket.gameObject, (NetworkConnectionToClient)null);
            ExplosionScaler.Register(rocket, Configuration.DonutCrashExplosionScale.Value);
            AC130Item.ServerExplodeRocket(rocket); // Borrow this method
        }

        // ================================================================
        //  Server → Client  (called by message handlers registered in
        //  NetworkManagerPatches)
        // ================================================================

        public void ClientBeginDonut(uint donutNetId)
        {
            StartCoroutine(RunLocalSession(donutNetId));
        }

        public void ClientEndDonut(bool shotDown)
        {
            _forceEnd = true;
            _shotDown = shotDown;
        }

        public void ClientDonutBusy()
        {
            IssaPluginPlugin.Log.LogInfo("[Donut] Donut is already in use by another player.");
        }

        public void ClientDonutStartShooting(Vector3 startPosition)
        {
            var go = Object.Instantiate(
                AssetLoader.DonutLaserZoneRed,
                startPosition,
                Quaternion.identity
            );

            localLaserUsesRemaining--;
            DonutOverlay.UpdateLaserUses(localLaserUsesRemaining);
            DonutOverlay.TriggerLaserFlash();
            Destroy(go, 3.0f); // Destroy after 3 seconds.
        }

        // ================================================================
        //  Server internals
        // ================================================================

        private void LaserAnticipationCoroutine()
        {
            _laserPending = true;

            // Fire at wherever the Donut ended up.
            if (_serverSessionActive && _serverDonut != null)
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
                        DonutLaserTargetVector,
                        inventory,
                        itemUseId
                    );
                    _laserUsesRemaining--;
                }
            }

            NetworkServer.SendToAll(
                new DonutLaserShootStartMessage { StartPosition = DonutLaserTargetVector }
            );

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
            connectionToClient.Send(new DonutEndClientMessage());

            if (_serverDonut != null)
            {
                NetworkServer.Destroy(_serverDonut);
                _serverDonut = null;
            }

            ReleaseGlobalLock();
            IssaPluginPlugin.Log.LogInfo("[Donut] Server session ended.");
        }

        public void ServerHoleCleanup()
        {
            if (_serverSessionActive)
                ForceServerCleanup();
        }

        public void ClientHoleCleanup()
        {
            if (LocalSessionActive)
                _forceEnd = true;
        }

        private void ForceServerCleanup()
        {
            if (_serverTimeout != null)
            {
                StopCoroutine(_serverTimeout);
                _serverTimeout = null;
            }

            if (_serverDonut != null)
            {
                NetworkServer.Destroy(_serverDonut);
                _serverDonut = null;
            }

            _serverSessionActive = false;
            ReleaseGlobalLock();
        }

        private static void ReleaseGlobalLock()
        {
            _globalSessionActive = false;
            _activeSessionBridge = null;
        }

        private IEnumerator ServerTimeoutRoutine()
        {
            yield return new WaitForSeconds(Configuration.DonutDuration.Value + 10f);
            if (_serverSessionActive)
                EndServerSession();
        }

        // ================================================================
        //  Client: local session coroutine
        // ================================================================

        private IEnumerator RunLocalSession(uint donutNetId)
        {
            LocalSessionActive = true;
            _forceEnd = false;
            _shotDown = false;

            InputManager.Controls.Gameplay.Disable();

            // Skip one frame so wasPressedThisFrame on the activation key
            // doesn't immediately read as an Escape / early-exit press.
            yield return null;

            // Wait for Mirror to spawn the Donut on this client.
            float waited = 0f;
            NetworkIdentity donutIdentity = null;
            while (!NetworkClient.spawned.TryGetValue(donutNetId, out donutIdentity) && waited < 2f)
            {
                waited += Time.deltaTime;
                yield return null;
            }

            if (donutIdentity == null)
            {
                IssaPluginPlugin.Log.LogError(
                    "[Donut] Donut not found in spawned dict after wait."
                );
                DonutOverlay.SetActive(false, 0);
                LocalSessionActive = false;
                InputManager.Controls.Gameplay.Enable();
                yield break;
            }

            Transform donutTransform = donutIdentity.transform;

            // ── Orbit camera setup ────────────────────────────────────────────
            CameraModuleController.TryGetOrbitModule(out var orbitModule);
            float savedPitch = orbitModule?.Pitch ?? 0f;
            float savedYaw = orbitModule?.Yaw ?? 0f;
            bool savedDisablePhysics = orbitModule?.disablePhysics ?? false;

            float cameraYaw = savedYaw;

            if (orbitModule != null)
            {
                orbitModule.SetSubject(donutTransform);
                orbitModule.SetPitch(Configuration.DonutCameraPitch.Value);
                orbitModule.SetDistanceAddition(Configuration.DonutCameraDistance.Value);
                orbitModule.disablePhysics = true;
                orbitModule.ForceUpdateModule();
            }

            localLaserUsesRemaining = (int)Configuration.DonutLaserUses.Value;
            float sessionElapsed = 0f;
            float sessionDuration = Configuration.DonutDuration.Value;

            DonutOverlay.SetActive(true, localLaserUsesRemaining);

            // ── Main control loop ─────────────────────────────────────────────
            while (!_forceEnd && sessionElapsed < sessionDuration)
            {
                sessionElapsed += Time.deltaTime;

                var mouse = Mouse.current;
                var keyboard = Keyboard.current;

                if (
                    !_canMove
                    && Time.time - _localLastLaserTime >= Configuration.DonutLaserCooldown.Value
                )
                {
                    _canMove = true;
                }

                if (keyboard != null && Keyboard.current[Key.Space].wasPressedThisFrame)
                {
                    IssaPluginPlugin.Log.LogInfo("[Donut] Donut cancelled by player.");
                    _forceEnd = true;
                    break;
                }

                // Mouse X → rotate orbit camera yaw.
                if (mouse != null)
                {
                    float mouseX =
                        mouse.delta.x.ReadValue() * Configuration.DonutMouseSensitivity.Value;
                    cameraYaw += mouseX;
                    if (cameraYaw >= 360f)
                        cameraYaw -= 360f;
                    if (cameraYaw < 0f)
                        cameraYaw += 360f;
                    orbitModule?.SetYaw(cameraYaw);
                }

                // Keep camera snapped to the Donut without lerp lag.
                orbitModule?.ForceUpdateModule();

                // WASD → camera-relative world-space move direction.
                float fwd = 0f,
                    strafe = 0f;
                if (keyboard != null && _canMove)
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

                // Rate-limit movement sends: only send when direction changed or
                // timer expires (20 Hz cap), matching the AC130 flight input pattern.
                bool moveChanged = (worldMoveDir - _lastSentMoveDir).sqrMagnitude > 0.001f;
                _moveSendTimer -= Time.deltaTime;
                if (moveChanged || _moveSendTimer <= 0f)
                {
                    NetworkClient.Send(new DonutMoveMessage { WorldMoveDir = worldMoveDir });
                    _lastSentMoveDir = worldMoveDir;
                    _moveSendTimer = InputSendInterval;
                }

                // Left click → fire laser.
                if (
                    mouse != null
                    && mouse.leftButton.wasPressedThisFrame
                    && localLaserUsesRemaining > 0
                    && Time.time - _localLastLaserTime >= Configuration.DonutLaserCooldown.Value
                )
                {
                    NetworkClient.Send(new DonutFireLaserMessage());
                    _localLastLaserTime = Time.time;
                    _canMove = false;
                }

                DonutOverlay.UpdateTimeRemaining(sessionDuration - sessionElapsed);

                yield return null;
            }

            // ── Session end ───────────────────────────────────────────────────
            // Stop the Donut before the server destroys it.
            // If we were shot down, then the server will handle things.
            if (!_shotDown)
            {
                NetworkClient.Send(new DonutMoveMessage { WorldMoveDir = Vector3.zero });
                NetworkClient.Send(new DonutEndMessage { ShouldCrash = true });
            }

            DonutOverlay.SetActive(false, 0);

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

            IssaPluginPlugin.Log.LogInfo("[Donut] Client session ended.");
        }
    }
}
