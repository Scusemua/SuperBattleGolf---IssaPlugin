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

        /// <summary>
        /// Server-side reference to the active gunship GameObject.
        /// Used by GunshipLockOnPatches to attach GunshipHomingBehaviour to rockets.
        /// </summary>
        public static GameObject ActiveGunship => _activeSessionBridge?._serverGunship;

        // ================================================================
        //  Per-instance server state
        // ================================================================

        private Coroutine _serverTimeout;
        private bool _serverSessionActive;
        private GameObject _serverGunship;
        private float _serverLastFireTime;

        /// <summary>
        /// Set by CmdPrepareGunshipRocket when the owning client has the gunship
        /// locked on. Consumed by GunshipRocketHomingPatch when the next rocket spawns.
        /// </summary>
        public bool PendingGunshipHoming;

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

        private void Update()
        {
            // Forward mayday input to the server every frame while the owning
            // client is in an active mayday. This keeps server-side dive physics
            // responsive to player input on both listen-server and dedicated-server.
            if (!LocalMaydayActive || !isOwned)
                return;

            var keyboard = Keyboard.current;
            float diveInfluence = 0f;
            float rollInfluence = 0f;

            if (keyboard != null)
            {
                if (keyboard[Key.W].isPressed || keyboard[Key.UpArrow].isPressed)
                    diveInfluence = -1f;
                if (keyboard[Key.S].isPressed || keyboard[Key.DownArrow].isPressed)
                    diveInfluence = 1f;
                if (keyboard[Key.A].isPressed || keyboard[Key.LeftArrow].isPressed)
                    rollInfluence = 1f;
                if (keyboard[Key.D].isPressed || keyboard[Key.RightArrow].isPressed)
                    rollInfluence = -1f;
            }

            CmdSetMaydayInput(diveInfluence, rollInfluence);
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
            RpcAddGunshipLockOnComponents(gunshipIdentity);
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
        public void CmdFireAC130(Vector3 aimDirection)
        {
            if (!_serverSessionActive || _serverGunship == null)
                return;

            // Server-side rate limit — the client enforces its own cooldown too,
            // but this prevents a lagging or malicious client from over-firing.
            if (Time.time - _serverLastFireTime < Configuration.AC130FireCooldown.Value)
                return;
            _serverLastFireTime = Time.time;

            var inventory = GetComponent<PlayerInventory>();
            if (inventory == null)
                return;

            float jitterDeg = Configuration.AC130RocketAngularJitter.Value;
            Quaternion jitter = Quaternion.Euler(
                Random.Range(-jitterDeg, jitterDeg),
                Random.Range(-jitterDeg, jitterDeg),
                0f
            );

            // Use the server's authoritative gunship position rather than the
            // client-provided one, which may be a stale approximation.
            Quaternion fireRotation = Quaternion.LookRotation(aimDirection, Vector3.up);
            AC130Item.SpawnRocketInDirection(
                inventory,
                _serverGunship.transform.position,
                jitter * fireRotation
            );
        }

        [Command]
        public void CmdTriggerMayday()
        {
            if (!_serverSessionActive)
                return;

            IssaPluginPlugin.Log.LogInfo("[AC130] Manual mayday triggered by player.");
            ServerBeginMayday();
        }

        /// <summary>
        /// Called by GunshipLockOnDetectionPatch while the player has the gunship
        /// locked on. Flags the server so the next rocket that spawns homes toward it.
        /// </summary>
        [Command]
        public void CmdPrepareGunshipRocket()
        {
            if (!_serverSessionActive || _serverGunship == null) return;
            PendingGunshipHoming = true;
        }

        /// <summary>
        /// Sent every frame by the owning client while LocalMaydayActive is true.
        /// Forwards keyboard input so the server-side AC130MaydayBehaviour can apply
        /// player pull and roll to the authoritative dive physics.
        /// diveInfluence: -1 = pull up, +1 = push down.
        /// rollInfluence: -1 = roll left, +1 = roll right.
        /// </summary>
        [Command]
        public void CmdSetMaydayInput(float diveInfluence, float rollInfluence)
        {
            if (!_serverSessionActive || _serverGunship == null)
                return;

            var mayday = _serverGunship.GetComponent<AC130MaydayBehaviour>();
            if (mayday == null)
                return;

            mayday.ExternalDiveInfluence = diveInfluence;
            mayday.ExternalRollInfluence = rollInfluence;
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

            // Explicitly initialise the cockpit camera and alarm now that IsLocalPlayer
            // is true. This cannot be done in Start() because on a listen server the
            // component is added by the server before this RPC runs, so Start() fires
            // with IsLocalPlayer=false and skips the camera/alarm setup.
            mayday.BeginAsLocalPlayer();

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

        /// <summary>
        /// Runs on all clients after the gunship is spawned. Adds the lightweight
        /// AC130GunshipMarker and LockOnTarget components so the game's lock-on
        /// manager can track the gunship and our Harmony patches can identify it.
        /// </summary>
        [ClientRpc(includeOwner = true)]
        private void RpcAddGunshipLockOnComponents(NetworkIdentity gunshipIdentity)
        {
            if (gunshipIdentity == null) return;
            AddGunshipLockOnComponents(gunshipIdentity.gameObject);
        }

        private static void AddGunshipLockOnComponents(GameObject go)
        {
            if (go.GetComponent<AC130GunshipMarker>() == null)
                go.AddComponent<AC130GunshipMarker>();

            // LockOnTarget registers with LockOnTargetManager in its Awake so that
            // PlayerGolfer.TryGetBestLockOnTarget iterates over the gunship.
            if (go.GetComponent<LockOnTarget>() == null)
                go.AddComponent<LockOnTarget>();
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

            // Always skip at least one frame before reading input, so that
            // wasPressedThisFrame on any key used to activate the item doesn't
            // immediately trigger the fly-in cancel / mayday checks below.
            yield return null;

            // Wait for Mirror to finish syncing the spawned gunship to this client.
            // In host mode this is instant; over a real network it may take a few frames.
            if (gunshipIdentity != null)
            {
                float waited = 0f;
                while (gunshipIdentity.gameObject == null && waited < 2f)
                {
                    waited += Time.deltaTime;
                    yield return null;
                }
            }

            GameObject gunshipGo = gunshipIdentity != null ? gunshipIdentity.gameObject : null;
            if (gunshipGo == null)
                IssaPluginPlugin.Log.LogError(
                    "[AC130] Gunship still null after waiting — camera will not activate."
                );

            var session = new AC130Session(inventory, gunshipGo, mapCentre);

            // ============================================================
            //  Phase 1: Fly-in
            //
            //  On a listen server, FlyComp exists (same object instance) and
            //  HasArrived is the authoritative completion signal.
            //  On a remote client, FlyComp is null (the component is added at
            //  runtime on the server and not synced to clients). We fall back to
            //  a time estimate so the player still sees the fly-in cinematic.
            // ============================================================
            bool hasFlyComp = session.FlyComp != null;
            bool hasGunshipVisual = session.GunshipVisual != null;

            if (hasFlyComp || hasGunshipVisual)
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

                float estimatedFlyInTime =
                    Configuration.AC130ApproachDistance.Value
                    / Configuration.AC130ApproachSpeed.Value;
                float flyInElapsed = 0f;

                while (!_forceEnd && !_maydayTriggered)
                {
                    // Completion: authoritative (listen server) or time-based (remote client).
                    if (
                        hasFlyComp ? session.FlyComp.HasArrived : flyInElapsed >= estimatedFlyInTime
                    )
                        break;

                    if (Keyboard.current != null && Keyboard.current[Key.Space].wasPressedThisFrame)
                    {
                        IssaPluginPlugin.Log.LogInfo("[AC130] Fly-in cancelled by player.");
                        _forceEnd = true;
                        break;
                    }

                    CheckMaydayHotkey();

                    if (session.GunshipVisual != null)
                        session.PivotGo.transform.position = session
                            .GunshipVisual
                            .transform
                            .position;
                    session.OrbitModule?.ForceUpdateModule();
                    flyInElapsed += Time.deltaTime;
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
                    CmdFireAC130(aimDirection);
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

            // The prefab may not have a NetworkIdentity baked in.
            // Adding one at runtime before NetworkServer.Spawn is valid
            // and is the only option when we can't modify the bundle prefab.
            if (go.GetComponent<NetworkIdentity>() == null)
                go.AddComponent<NetworkIdentity>();

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

            // Add lock-on components on the server immediately after spawn.
            // RpcAddGunshipLockOnComponents will mirror this on all clients.
            AddGunshipLockOnComponents(go);

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
            // Capture mapCentre BEFORE destroying the fly component.
            var flyComp = _serverGunship.GetComponent<AC130FlyBehaviour>();
            Vector3 mapCentre = flyComp != null ? flyComp.mapCentre : _serverGunship.transform.position;
            if (flyComp != null)
            {
                flyComp.OnExternallyDestroyed = null; // prevent re-entry
                Object.Destroy(flyComp);
            }

            var gunshipIdentity = _serverGunship.GetComponent<NetworkIdentity>();

            // Add the authoritative mayday driver on the server.
            var maydayComp = _serverGunship.AddComponent<AC130MaydayBehaviour>();
            maydayComp.IsLocalPlayer = false;
            maydayComp.MapCentre = mapCentre;
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

            // VFX, screen shake, and audio on all clients via RPC.
            RpcPlayMaydayImpactVfx(impactPos);

            ServerSpawnImpactRocket(impactPos);

            if (_serverGunship != null)
            {
                Object.Destroy(_serverGunship);
                _serverGunship = null;
            }

            _serverSessionActive = false;
            ReleaseGlobalLock();
            TargetEndMayday(connectionToClient);
        }

        [ClientRpc]
        private void RpcPlayMaydayImpactVfx(Vector3 impactPos)
        {
            float duration = Configuration.AC130MaydayExplosionDuration.Value;

            if (AssetLoader.MaydayExplosionVfxPrefab != null)
            {
                var vfxGo = Object.Instantiate(
                    AssetLoader.MaydayExplosionVfxPrefab,
                    impactPos,
                    Quaternion.identity
                );

                // Fade out any lights in the prefab over the explosion duration.
                // ETFXLightFade can't be bundled (scripts aren't asset-bundle-able),
                // so we attach our own fader from the mod DLL instead.
                // LightFader uses GetComponent<Light>() in Start(), so it must be
                // added to the same GameObject as the Light, and life is set before
                // Start() runs (AddComponent calls Awake immediately, Start is deferred).
                // foreach (var light in vfxGo.GetComponentsInChildren<Light>())
                // {
                //     var fader = light.gameObject.AddComponent<LightFader>();
                //     fader.life = duration;
                // }

                Object.Destroy(vfxGo, duration);
            }
            else
            {
                // Fallback: use the game's pooled VFX locally.
                VfxManager.PlayPooledVfxLocalOnly(
                    VfxType.RocketLauncherRocketExplosion,
                    impactPos,
                    Quaternion.identity,
                    Vector3.one * Configuration.AC130MaydayExplosionScale.Value
                );
            }

            // Secondary lingering smoke/debris at the crash site (optional bundle asset).
            if (AssetLoader.AC130ImpactVfxPrefab != null)
            {
                var smokeGo = Object.Instantiate(
                    AssetLoader.AC130ImpactVfxPrefab,
                    impactPos,
                    Quaternion.identity
                );
                Object.Destroy(smokeGo, duration);
            }

            // Screen shake on all clients, including those on a dedicated server.
            CameraModuleController.Shake(
                GameManager.CameraGameplaySettings.RocketExplosionScreenshakeSettings,
                impactPos
            );
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
