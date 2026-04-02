using System.Collections;
using IssaPlugin.Network;
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
    /// </summary>
    public class DonutNetworkBridge : NetworkBridgeBase
    {
        // ================================================================
        //  Global server lock  (server only, static)
        // ================================================================

        // Transform whose position is repeatedly set to the raycast
        // to the ground by the Donut.
        private static Transform _donutLaserTarget = null;

        /// Server-side reference to the active Donut GameObject.
        public static GameObject ActiveDonut =>
            GlobalSessionLock<DonutNetworkBridge>.Holder?._serverDonut;

        public static Transform DonutLaserTarget
        {
            get => _donutLaserTarget;
            set { _donutLaserTarget = value; }
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
        private DonutFlyBehaviour _serverFlyBehaviour;
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

        private int _localLaserUsesRemaining;
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

            if (GlobalSessionLock<DonutNetworkBridge>.IsActive)
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
            if (equipped != ItemRegistry.DonutItemType)
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
                + Vector3.up * ModConfig.Donut.Altitude.Value;

            var donutGo = Object.Instantiate(
                AssetLoader.DonutPrefab,
                spawnPos,
                Quaternion.identity
            );

            _serverFlyBehaviour = donutGo.AddComponent<DonutFlyBehaviour>();
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
            _ = GlobalSessionLock<DonutNetworkBridge>.TryAcquire(this);

            _laserUsesRemaining = (int)ModConfig.Donut.LaserUses.Value;
            _laserUseIndex = 0;
            _laserPending = false;
            _lastLaserTime = -999f;

            ItemWarningBroadcaster.Broadcast(
                inventory.PlayerInfo.PlayerId.PlayerName,
                ItemRegistry.DonutItemType,
                "Donut",
                trackedNetId: ni.netId,
                senderNetId:  netId
            );
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

            if (_serverFlyBehaviour != null)
                _serverFlyBehaviour.MoveInput = worldMoveDir;
        }

        public void ServerFireLaser()
        {
            if (!_serverSessionActive || _serverDonut == null)
                return;

            if (_laserPending)
                return;

            if (_laserUsesRemaining <= 0)
                return;

            if (Time.time - _lastLaserTime < ModConfig.Donut.LaserCooldown.Value)
                return;

            _lastLaserTime = Time.time;
            ExecuteLaserFire();
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

            if (_serverFlyBehaviour != null)
                _serverFlyBehaviour.enabled = false;

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

            if (rocket == null)
            {
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
            ExplosionScaler.Register(rocket, ModConfig.Donut.CrashExplosionScale.Value);
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
            // var go = Object.Instantiate(
            //     AssetLoader.DonutLaserZoneRed,
            //     startPosition,
            //     Quaternion.identity
            // );

            _localLaserUsesRemaining--;
            DonutOverlay.UpdateLaserUses(_localLaserUsesRemaining);
            DonutOverlay.TriggerLaserFlash();
            // Destroy(go, 3.0f); // Destroy after 3 seconds.
        }

        // ================================================================
        //  Server internals
        // ================================================================

        private void ExecuteLaserFire()
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

            _serverFlyBehaviour = null;
            ReleaseGlobalLock();
            IssaPluginPlugin.Log.LogInfo("[Donut] Server session ended.");
        }

        public override void ServerHoleCleanup()
        {
            if (_serverSessionActive)
                ForceServerCleanup();
        }

        public override void ClientHoleCleanup() // Runs on client
        {
            if (LocalSessionActive)
            {
                _forceEnd = true;
                DonutOverlay.SetActive(false, 0);
                LocalSessionActive = false;
                InputManager.Controls.Gameplay.Enable();
            }
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

            _serverFlyBehaviour = null;
            _serverSessionActive = false;
            ReleaseGlobalLock();
        }

        private static void ReleaseGlobalLock() =>
            GlobalSessionLock<DonutNetworkBridge>.Release();

        public static void ForceReleaseGlobalLock()
        {
            IssaPluginPlugin.Log.LogWarning(
                "[Donut] ForceReleaseGlobalLock called — resetting session state."
            );
            GlobalSessionLock<DonutNetworkBridge>.Release();
        }

        private IEnumerator ServerTimeoutRoutine()
        {
            yield return new WaitForSeconds(ModConfig.Donut.Duration.Value + 10f);
            if (_serverSessionActive)
                EndServerSession();
        }

        // ================================================================
        //  Client: local session coroutine
        // ================================================================

        private readonly struct DonutCameraState
        {
            public readonly OrbitCameraModule OrbitModule;
            public readonly float SavedPitch;
            public readonly float SavedYaw;
            public readonly bool SavedDisablePhysics;

            public DonutCameraState(
                OrbitCameraModule module,
                float pitch,
                float yaw,
                bool disablePhysics
            )
            {
                OrbitModule = module;
                SavedPitch = pitch;
                SavedYaw = yaw;
                SavedDisablePhysics = disablePhysics;
            }
        }

        private static DonutCameraState SetupOrbitCamera(Transform donutTransform)
        {
            CameraModuleController.TryGetOrbitModule(out var orbitModule);
            float savedPitch = orbitModule?.Pitch ?? 0f;
            float savedYaw = orbitModule?.Yaw ?? 0f;
            bool savedPhys = orbitModule?.disablePhysics ?? false;

            if (orbitModule != null)
            {
                orbitModule.SetSubject(donutTransform);
                orbitModule.SetPitch(ModConfig.Donut.CameraPitch.Value);
                orbitModule.SetDistanceAddition(ModConfig.Donut.CameraDistance.Value);
                orbitModule.disablePhysics = true;
                orbitModule.ForceUpdateModule();
            }

            return new DonutCameraState(orbitModule, savedPitch, savedYaw, savedPhys);
        }

        private static void TeardownOrbitCamera(DonutCameraState state)
        {
            if (state.OrbitModule == null)
                return;
            state.OrbitModule.SetDistanceAddition(0f);
            state.OrbitModule.disablePhysics = state.SavedDisablePhysics;
            var playerMovement = GameManager.LocalPlayerMovement;
            if (playerMovement != null)
                state.OrbitModule.SetSubject(playerMovement.transform);
            state.OrbitModule.SetPitch(state.SavedPitch);
            state.OrbitModule.SetYaw(state.SavedYaw);
            state.OrbitModule.ForceUpdateModule();
        }

        /// <summary>
        /// Reads one frame of input, sends network messages as side effects, and
        /// returns false if the session should end immediately (Space pressed).
        /// Escape sets _forceEnd and returns true so the current frame's remaining
        /// work (movement send, overlay update) completes before the while condition
        /// exits — matching the original fall-through behavior.
        /// </summary>
        private bool ProcessDonutInput(OrbitCameraModule orbitModule, ref float cameraYaw)
        {
            var mouse = Mouse.current;
            var keyboard = Keyboard.current;

            // Re-enable movement after laser cooldown.
            if (!_canMove && Time.time - _localLastLaserTime >= ModConfig.Donut.LaserCooldown.Value)
                _canMove = true;

            // Space → cancel immediately (skips remaining frame work, matches original break).
            if (keyboard != null && keyboard[Key.Space].wasPressedThisFrame)
            {
                IssaPluginPlugin.Log.LogInfo("[Donut] Donut cancelled by player.");
                _forceEnd = true;
                return false;
            }

            // Mouse X → rotate orbit yaw.
            if (mouse != null)
            {
                float mouseX = mouse.delta.x.ReadValue() * ModConfig.Donut.MouseSensitivity.Value;
                cameraYaw += mouseX;
                if (cameraYaw >= 360f)
                    cameraYaw -= 360f;
                if (cameraYaw < 0f)
                    cameraYaw += 360f;
                orbitModule?.SetYaw(cameraYaw);
            }

            // Keep camera snapped to the Donut without lerp lag.
            orbitModule?.ForceUpdateModule();

            // WASD → world-space move direction (only when _canMove).
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

                // Escape → set flag and fall through; while condition exits next frame (matches original).
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
                && _localLaserUsesRemaining > 0
                && Time.time - _localLastLaserTime >= ModConfig.Donut.LaserCooldown.Value
            )
            {
                NetworkClient.Send(new DonutFireLaserMessage());
                _localLastLaserTime = Time.time;
                _canMove = false;
            }

            return true;
        }

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

            var cameraState = SetupOrbitCamera(donutIdentity.transform);
            float cameraYaw = cameraState.SavedYaw;

            _localLaserUsesRemaining = (int)ModConfig.Donut.LaserUses.Value;
            float sessionElapsed = 0f;
            float sessionDuration = ModConfig.Donut.Duration.Value;

            DonutOverlay.SetActive(true, _localLaserUsesRemaining);

            // ── Main control loop ─────────────────────────────────────────────
            while (!_forceEnd && sessionElapsed < sessionDuration)
            {
                sessionElapsed += Time.deltaTime;
                if (!ProcessDonutInput(cameraState.OrbitModule, ref cameraYaw))
                    break;
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
            TeardownOrbitCamera(cameraState);
            InputManager.Controls.Gameplay.Enable();
            LocalSessionActive = false;

            IssaPluginPlugin.Log.LogInfo("[Donut] Client session ended.");
        }
    }
}
