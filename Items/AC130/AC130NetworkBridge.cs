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
    /// The gunship is spawned on the server inside CmdStartAC130 so the
    /// server always holds a valid reference for mayday / cleanup.
    /// Its NetworkIdentity is passed to the owning client via TargetBeginAC130
    /// so the client can attach camera components to the same object.
    ///
    /// Global one-at-a-time lock: only one AC130 session may be active
    /// across all players at once (any phase: fly-in, on-station, fly-out,
    /// mayday). Lock lives in static server-side fields.
    /// </summary>
    public class AC130NetworkBridge : NetworkBehaviour
    {
        // ================================================================
        //  Global server lock  (server only, static)
        // ================================================================

        private static bool _globalSessionActive;
        private static AC130NetworkBridge _activeSessionBridge;

        // ================================================================
        //  Per-instance server state
        // ================================================================

        private Coroutine _serverTimeout;
        private bool _serverSessionActive;
        private GameObject _serverGunship;

        // ================================================================
        //  Per-instance client state  (owning client only)
        // ================================================================

        public bool LocalSessionActive { get; private set; }
        public bool LocalMaydayActive { get; private set; }

        private bool _forceEnd;
        private bool _maydayTriggered;

        public Camera LocalGunshipCamera { get; private set; }

        // ================================================================
        //  Mirror lifecycle
        // ================================================================

        public override void OnStopServer()
        {
            if (_serverSessionActive)
            {
                IssaPluginPlugin.Log.LogInfo(
                    "[AC130] Player disconnected during session — forcing cleanup."
                );
                ForceServerCleanup();
            }
        }

        // ================================================================
        //  Client → Server
        // ================================================================

        [Command]
        public void CmdStartAC130()
        {
            if (_serverSessionActive)
            {
                IssaPluginPlugin.Log.LogWarning("[AC130] Session already active for this player.");
                return;
            }

            if (_globalSessionActive)
            {
                IssaPluginPlugin.Log.LogWarning(
                    "[AC130] Another player's AC130 is already active."
                );
                TargetAC130Busy(connectionToClient);
                return;
            }

            var inventory = GetComponent<PlayerInventory>();
            if (inventory == null)
                return;

            var equipped = inventory.GetEffectivelyEquippedItem(true);
            if (equipped != AC130Item.AC130ItemType)
            {
                IssaPluginPlugin.Log.LogWarning("[AC130] Player does not have AC130 equipped.");
                return;
            }

            ItemHelper.ConsumeEquippedItem(inventory);

            // ----------------------------------------------------------------
            //  Spawn the gunship on the server so we hold a valid reference
            //  for mayday detection, external destruction, and disconnect cleanup.
            // ----------------------------------------------------------------
            Vector3 mapCentre = inventory.PlayerInfo.transform.position;
            GameObject gunshipGo = ServerSpawnGunship(mapCentre);

            if (gunshipGo == null)
            {
                IssaPluginPlugin.Log.LogError("[AC130] Failed to spawn gunship — aborting.");
                return;
            }

            _serverGunship = gunshipGo;
            _serverSessionActive = true;
            _globalSessionActive = true;
            _activeSessionBridge = this;

            // Wire up external-destruction callback on the fly behaviour.
            var flyComp = gunshipGo.GetComponent<AC130FlyBehaviour>();
            if (flyComp != null)
            {
                flyComp.OnExternallyDestroyed = () =>
                {
                    if (_serverSessionActive)
                    {
                        IssaPluginPlugin.Log.LogInfo(
                            "[AC130] Gunship destroyed externally — triggering mayday."
                        );
                        ServerBeginMayday();
                    }
                };
            }

            var gunshipIdentity = gunshipGo.GetComponent<NetworkIdentity>();

            RpcPlayAC130Sound();
            TargetBeginAC130(connectionToClient, gunshipIdentity, mapCentre);
            _serverTimeout = StartCoroutine(ServerTimeoutRoutine());

            IssaPluginPlugin.Log.LogInfo("[AC130] Server session started.");
        }

        [Command]
        public void CmdEndAC130()
        {
            EndServerSession();
        }

        [Command]
        public void CmdFireAC130(Vector3 position, Vector3 aimDirection)
        {
            if (!_serverSessionActive)
                return;

            var inventory = GetComponent<PlayerInventory>();
            if (inventory == null)
                return;

            float jitterDeg = Configuration.AC130RocketAngularJitter.Value;
            Quaternion jitter = Quaternion.Euler(
                Random.Range(-jitterDeg, jitterDeg),
                Random.Range(-jitterDeg, jitterDeg),
                0f
            );

            Quaternion fireRotation = Quaternion.LookRotation(aimDirection, Vector3.up);
            AC130Item.SpawnRocketInDirection(inventory, position, jitter * fireRotation);
        }

        [Command]
        public void CmdTriggerMayday()
        {
            if (!_serverSessionActive)
                return;

            IssaPluginPlugin.Log.LogInfo("[AC130] Manual mayday triggered by player.");
            ServerBeginMayday();
        }

        // ================================================================
        //  Server → Client
        // ================================================================

        [TargetRpc]
        public void TargetBeginAC130(
            NetworkConnection target,
            NetworkIdentity gunshipIdentity,
            Vector3 mapCentre
        )
        {
            StartCoroutine(
                RunLocalSession(GetComponent<PlayerInventory>(), gunshipIdentity, mapCentre)
            );
        }

        [TargetRpc]
        public void TargetEndAC130(NetworkConnection target)
        {
            _forceEnd = true;
        }

        [TargetRpc]
        public void TargetBeginMayday(NetworkConnection target, NetworkIdentity gunshipIdentity)
        {
            if (gunshipIdentity == null)
            {
                IssaPluginPlugin.Log.LogError("[Mayday] gunshipIdentity is null on client.");
                return;
            }

            _maydayTriggered = true;

            var gunship = gunshipIdentity.gameObject;
            var mayday =
                gunship.GetComponent<AC130MaydayBehaviour>()
                ?? gunship.AddComponent<AC130MaydayBehaviour>();
            mayday.IsLocalPlayer = true;
            mayday.MapCentre = gunship.GetComponent<AC130FlyBehaviour>()?.mapCentre ?? Vector3.zero;

            LocalMaydayActive = true;
            AC130Overlay.SetMaydayActive(true);

            IssaPluginPlugin.Log.LogInfo("[Mayday] Cockpit cinematic started on owning client.");
        }

        [ClientRpc]
        public void RpcBeginMaydayVfx(NetworkIdentity gunshipIdentity)
        {
            if (gunshipIdentity == null || isOwned)
                return;

            var gunship = gunshipIdentity.gameObject;
            if (gunship.GetComponent<AC130MaydayBehaviour>() == null)
            {
                var mayday = gunship.AddComponent<AC130MaydayBehaviour>();
                mayday.IsLocalPlayer = false;
                mayday.MapCentre =
                    gunship.GetComponent<AC130FlyBehaviour>()?.mapCentre ?? Vector3.zero;
            }
        }

        [TargetRpc]
        public void TargetEndMayday(NetworkConnection target)
        {
            LocalMaydayActive = false;
            LocalGunshipCamera = null;
            LocalSessionActive = false;
            AC130Overlay.SetMaydayActive(false);
            InputManager.Controls.Gameplay.Enable();
            IssaPluginPlugin.Log.LogInfo("[Mayday] Client mayday ended.");
        }

        [TargetRpc]
        private void TargetAC130Busy(NetworkConnection target)
        {
            IssaPluginPlugin.Log.LogInfo("[AC130] AC130 is already in use by another player.");
            // TODO: surface a HUD notification to the player.
        }

        [ClientRpc(includeOwner = true)]
        private void RpcPlayAC130Sound()
        {
            var clip = AssetLoader.AC130AboveClip;
            if (clip == null)
            {
                IssaPluginPlugin.Log.LogWarning("[AC130] Audio clip not loaded.");
                return;
            }

            var go = new GameObject("AC130_Sound");
            var src = go.AddComponent<AudioSource>();
            src.clip = clip;
            src.spatialBlend = 0f;
            src.volume = 1f;
            src.Play();
            Destroy(go, clip.length + 0.1f);
        }

        // ================================================================
        //  Local session — runs only on the owning client
        // ================================================================

        private IEnumerator RunLocalSession(
            PlayerInventory inventory,
            NetworkIdentity gunshipIdentity,
            Vector3 mapCentre
        )
        {
            LocalSessionActive = true;
            _forceEnd = false;
            _maydayTriggered = false;

            InputManager.Controls.Gameplay.Disable();

            // Wait one frame for Mirror to finish syncing the spawned gunship
            // to this client before we try to access its components.
            yield return null;

            GameObject gunshipGo = gunshipIdentity != null ? gunshipIdentity.gameObject : null;
            var session = new AC130Session(inventory, gunshipGo, mapCentre);

            // ============================================================
            //  Phase 1: Fly-in
            // ============================================================
            if (session.FlyComp != null)
            {
                if (session.OrbitModule != null)
                {
                    session.OrbitModule.SetSubject(session.PivotGo.transform);
                    session.OrbitModule.SetPitch(Configuration.AC130CameraPitch.Value);
                    session.OrbitModule.SetDistanceAddition(
                        Configuration.AC130CameraDistance.Value
                    );
                    session.OrbitModule.disablePhysics = true;
                }

                IssaPluginPlugin.Log.LogInfo("[AC130] Fly-in phase started.");

                while (!session.FlyComp.HasArrived && !_forceEnd && !_maydayTriggered)
                {
                    if (Keyboard.current != null && Keyboard.current[Key.Space].wasPressedThisFrame)
                    {
                        IssaPluginPlugin.Log.LogInfo("[AC130] Fly-in cancelled by player.");
                        _forceEnd = true;
                        break;
                    }

                    CheckMaydayHotkey();

                    session.PivotGo.transform.position = session.GunshipVisual.transform.position;
                    session.OrbitModule?.ForceUpdateModule();
                    yield return null;
                }

                IssaPluginPlugin.Log.LogInfo("[AC130] Fly-in complete.");
            }

            // Cancelled during fly-in.
            if (_forceEnd && !_maydayTriggered)
            {
                session.Cleanup();
                LocalGunshipCamera = null;
                LocalSessionActive = false;
                CmdEndAC130();
                yield break;
            }

            // Mayday during fly-in.
            if (_maydayTriggered)
            {
                session.CleanupForMayday();
                LocalGunshipCamera = null;
                yield return WaitForMaydayEnd();
                yield break;
            }

            // ============================================================
            //  Phase 2: On-station
            // ============================================================
            session.BeginGunshipView();
            LocalGunshipCamera = session.GunshipCam?.Camera;

            while (session.Elapsed < session.Duration && !_forceEnd && !_maydayTriggered)
            {
                session.Elapsed += Time.deltaTime;
                session.Cooldown -= Time.deltaTime;

                var keyboard = Keyboard.current;
                var mouse = Mouse.current;

                if (keyboard != null && keyboard[Key.Space].wasPressedThisFrame)
                {
                    IssaPluginPlugin.Log.LogInfo("[AC130] Player exited early.");
                    break;
                }

                CheckMaydayHotkey();

                AC130Item.HandleFlight(keyboard, session);
                session.GunshipCam?.UpdateLook();

                float currentAngle =
                    session.FlyComp != null
                        ? session.FlyComp.currentAngle
                        : session.Elapsed * session.BaseOrbitSpeed;

                Vector3 gunshipPos = AC130Helpers.OrbitPosition(
                    session.MapCentre,
                    currentAngle,
                    session.OrbitRadius,
                    session.Altitude + session.AltitudeOffset
                );

                AC130Item.HandleZoom(mouse, session);

                Vector3 crosshairWorld = gunshipPos;
                Vector3 aimDirection = Vector3.down;

                var gunshipCam = session.GunshipCam?.Camera;
                if (gunshipCam != null)
                {
                    Vector3 camPos = gunshipCam.transform.position;
                    Vector3 camForward = gunshipCam.transform.forward;

                    if (
                        Physics.Raycast(
                            camPos,
                            camForward,
                            out RaycastHit hit,
                            5000f,
                            AC130Item.GroundLayerMask
                        )
                    )
                        crosshairWorld = hit.point;
                    else
                        crosshairWorld = AC130Item.ProjectAimToGround(camPos, camForward);

                    aimDirection = (crosshairWorld - gunshipPos).normalized;
                }

                AC130Overlay.UpdateAimInfo(crosshairWorld, session.Elapsed, session.Duration);

                bool firePressed = mouse != null && mouse.leftButton.wasPressedThisFrame;
                if (firePressed && session.Cooldown <= 0f)
                {
                    CmdFireAC130(gunshipPos, aimDirection);
                    session.Cooldown = session.FireCooldown;
                    session.GunshipCam?.TriggerFireShake();
                    IssaPluginPlugin.Log.LogInfo($"[AC130] Rocket fired toward {crosshairWorld}.");
                }

                yield return null;
            }

            // Mayday triggered during on-station.
            if (_maydayTriggered)
            {
                session.CleanupForMayday();
                LocalGunshipCamera = null;
                yield return WaitForMaydayEnd();
                yield break;
            }

            // ============================================================
            //  Phase 3: Normal fly-out
            // ============================================================
            session.Cleanup();
            LocalGunshipCamera = null;
            LocalSessionActive = false;
            CmdEndAC130();

            IssaPluginPlugin.Log.LogInfo("[AC130] Session ended, gunship flying out.");
        }

        private void CheckMaydayHotkey()
        {
            if (!Configuration.AC130MaydayEnabled.Value)
                return;

            var keyboard = Keyboard.current;
            if (keyboard == null)
                return;

            if (keyboard[Configuration.AC130MaydayKey.Value].wasPressedThisFrame)
            {
                IssaPluginPlugin.Log.LogInfo("[AC130] Manual mayday hotkey pressed.");
                CmdTriggerMayday();
            }
        }

        private IEnumerator WaitForMaydayEnd()
        {
            while (LocalMaydayActive)
                yield return null;
        }

        // ================================================================
        //  Server: gunship spawning
        // ================================================================

        private static GameObject ServerSpawnGunship(Vector3 mapCentre)
        {
            if (AssetLoader.AC130Prefab == null)
            {
                IssaPluginPlugin.Log.LogWarning(
                    "[AC130] AC130 prefab not loaded — session will run without visual."
                );
                return null;
            }

            float startAngle = 0f;
            float altitude = Configuration.AC130Altitude.Value;
            float orbitRadius = Configuration.AC130OrbitRadius.Value;

            Vector3 orbitEntry = AC130Helpers.OrbitPosition(
                mapCentre,
                startAngle,
                orbitRadius,
                altitude
            );
            Vector3 approachDir = AC130Helpers.OrbitTangent(startAngle);
            float approachDist = Configuration.AC130ApproachDistance.Value;
            float approachSpeed = Configuration.AC130ApproachSpeed.Value;

            Vector3 spawnPos = orbitEntry - approachDir * approachDist;

            var go = Object.Instantiate(
                AssetLoader.AC130Prefab,
                spawnPos,
                Quaternion.LookRotation(approachDir, Vector3.up)
            );

            var flyComp = go.AddComponent<AC130FlyBehaviour>();
            flyComp.mapCentre = mapCentre;
            flyComp.orbitRadius = orbitRadius;
            flyComp.altitude = altitude;
            flyComp.orbitSpeed = Configuration.AC130OrbitSpeed.Value;
            flyComp.currentAngle = startAngle;
            flyComp.flyTarget = orbitEntry;
            flyComp.flySpeed = approachSpeed;
            flyComp.mode = AC130FlightMode.FlyIn;

            NetworkServer.Spawn(go);

            IssaPluginPlugin.Log.LogInfo(
                $"[AC130] Gunship spawned at approach distance {approachDist:F0}m."
            );

            return go;
        }

        // ================================================================
        //  Server: mayday
        // ================================================================

        private void ServerBeginMayday()
        {
            IssaPluginPlugin.Log.LogInfo(
                $"[AC130] ServerBeginMayday: session={_serverSessionActive}, "
                    + $"gunship={_serverGunship != null}, "
                    + $"isServer={NetworkServer.active}, "
                    + $"isOwned={isOwned}"
            );

            if (!_serverSessionActive || _serverGunship == null)
            {
                IssaPluginPlugin.Log.LogWarning(
                    $"[AC130] ServerBeginMayday guard hit — aborting. "
                        + $"session={_serverSessionActive}, gunship={_serverGunship != null}"
                );
                return;
            }

            if (_serverTimeout != null)
            {
                StopCoroutine(_serverTimeout);
                _serverTimeout = null;
            }

            // Stop normal flight — mayday takes over movement.
            var flyComp = _serverGunship.GetComponent<AC130FlyBehaviour>();
            if (flyComp != null)
            {
                flyComp.OnExternallyDestroyed = null; // prevent re-entry
                Object.Destroy(flyComp);
            }

            var gunshipIdentity = _serverGunship.GetComponent<NetworkIdentity>();

            // Add the authoritative mayday driver on the server.
            var maydayComp = _serverGunship.AddComponent<AC130MaydayBehaviour>();
            maydayComp.IsLocalPlayer = false;
            maydayComp.MapCentre = _serverGunship.transform.position; // dive toward map centre
            maydayComp.OnImpact = () => ServerHandleMaydayImpact(_serverGunship.transform.position);

            // Owning client gets the cockpit camera.
            TargetBeginMayday(connectionToClient, gunshipIdentity);

            // All other clients get smoke trail.
            RpcBeginMaydayVfx(gunshipIdentity);

            IssaPluginPlugin.Log.LogInfo("[AC130] Server mayday sequence started.");
        }

        private void ServerHandleMaydayImpact(Vector3 impactPos)
        {
            IssaPluginPlugin.Log.LogInfo($"[Mayday] Impact at {impactPos}.");

            if (AssetLoader.MaydayExplosionVfxPrefab != null)
            {
                var vfxGo = Object.Instantiate(
                    AssetLoader.MaydayExplosionVfxPrefab,
                    impactPos,
                    Quaternion.identity
                );
                NetworkServer.Spawn(vfxGo);
                Object.Destroy(vfxGo, 5f);
            }
            else
            {
                VfxManager.PlayPooledVfxLocalOnly(
                    VfxType.RocketLauncherRocketExplosion,
                    impactPos,
                    Quaternion.identity,
                    Vector3.one * Configuration.AC130MaydayExplosionScale.Value
                );
            }

            CameraModuleController.Shake(
                GameManager.CameraGameplaySettings.RocketExplosionScreenshakeSettings,
                impactPos
            );

            ServerSpawnImpactRocket(impactPos);

            // Destroy the gunship — no persistent wreck for now.
            // To add a wreck later: spawn a wreck prefab here before Destroy.
            if (_serverGunship != null)
            {
                Object.Destroy(_serverGunship);
                _serverGunship = null;
            }

            _serverSessionActive = false;
            ReleaseGlobalLock();
            TargetEndMayday(connectionToClient);
        }

        private void ServerSpawnImpactRocket(Vector3 position)
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
                return;

            var itemUseId = new ItemUseId(
                inventory.PlayerInfo.PlayerId.Guid,
                int.MaxValue,
                ItemType.RocketLauncher
            );

            rocket.ServerInitialize(inventory.PlayerInfo, null, itemUseId);
            NetworkServer.Spawn(rocket.gameObject, (NetworkConnectionToClient)null);
            ExplosionScaler.Register(rocket, Configuration.AC130MaydayExplosionScale.Value);
            AC130Item.ServerExplodeRocket(rocket);
        }

        // ================================================================
        //  Server internals
        // ================================================================

        private IEnumerator ServerTimeoutRoutine()
        {
            yield return new WaitForSeconds(Configuration.AC130Duration.Value + 5f);
            if (_serverSessionActive)
                EndServerSession();
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
            ReleaseGlobalLock();
            TargetEndAC130(connectionToClient);
            IssaPluginPlugin.Log.LogInfo("[AC130] Server session ended.");
        }

        private void ForceServerCleanup()
        {
            if (_serverTimeout != null)
            {
                StopCoroutine(_serverTimeout);
                _serverTimeout = null;
            }

            if (_serverGunship != null)
            {
                Object.Destroy(_serverGunship);
                _serverGunship = null;
            }

            _serverSessionActive = false;
            ReleaseGlobalLock();
        }

        private static void ReleaseGlobalLock()
        {
            _globalSessionActive = false;
            _activeSessionBridge = null;
        }
    }
}
