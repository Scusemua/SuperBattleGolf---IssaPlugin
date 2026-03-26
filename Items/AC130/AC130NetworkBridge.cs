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
    /// The gunship is spawned on the server inside CmdStartAC130 so the
    /// server always holds a valid reference for mayday / cleanup.
    /// Its NetworkIdentity is passed to the owning client via TargetBeginAC130
    /// so the client can attach camera components to the same object.
    ///
    /// Global one-at-a-time lock: only one AC130 session may be active
    /// across all players at once (any phase: fly-in, on-station, fly-out,
    /// mayday). Lock lives in static server-side fields.
    /// </summary>
    public class AC130NetworkBridge : NetworkBridgeBase
    {
        // ================================================================
        //  Global server lock  (server only, static)
        // ================================================================

        private static bool _globalSessionActive;
        private static AC130NetworkBridge _activeSessionBridge;

        /// Server-side reference to the active gunship GameObject.
        public static GameObject ActiveGunship => _activeSessionBridge?._serverGunship;

        // ================================================================
        //  Per-instance server state
        // ================================================================

        private Coroutine _serverTimeout;
        private bool _serverSessionActive;
        private GameObject _serverGunship;
        private AC130FlyBehaviour _serverFlyBehaviour;
        private AC130MaydayBehaviour _serverMaydayBehaviour;
        private float _serverLastFireTime;

        /// <summary>
        /// Set by CmdPrepareGunshipRocket when the owning client has the gunship
        /// locked on. Consumed by RocketHomingPatch when the next rocket spawns.
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

        // Rate-limiting state for per-frame sends
        private float _maydayInputSendTimer;
        private float _lastMaydayDive;
        private float _lastMaydayRoll;
        private float _flightInputSendTimer;
        private float _lastAltitudeOffset = float.MinValue;
        private bool _lastBoosting;

        private const float InputSendInterval = 0.05f; // 20 Hz cap

        private void Update()
        {
            // Forward mayday input to the server while the owning client is in mayday.
            // Rate-limited to 20 Hz and only sent when input actually changes, so
            // the common case of no keys held (0, 0) generates at most one packet
            // per interval rather than one per frame.
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

            bool changed = diveInfluence != _lastMaydayDive || rollInfluence != _lastMaydayRoll;
            _maydayInputSendTimer -= Time.deltaTime;

            if (changed || _maydayInputSendTimer <= 0f)
            {
                NetworkClient.Send(
                    new AC130MaydayInputMessage
                    {
                        DiveInfluence = diveInfluence,
                        RollInfluence = rollInfluence,
                    }
                );
                _lastMaydayDive = diveInfluence;
                _lastMaydayRoll = rollInfluence;
                _maydayInputSendTimer = InputSendInterval;
            }
        }

        // ================================================================
        //  Client → Server
        // ================================================================

        public void ServerStartAC130()
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
                connectionToClient.Send(new AC130BusyMessage());
                return;
            }

            var inventory = GetComponent<PlayerInventory>();
            if (inventory == null)
                return;

            var equipped = inventory.GetEffectivelyEquippedItem(true);
            if (equipped != ItemRegistry.AC130ItemType)
            {
                IssaPluginPlugin.Log.LogWarning("[AC130] Player does not have AC130 equipped.");
                return;
            }

            ItemHelper.ConsumeEquippedItem(inventory);

            // ----------------------------------------------------------------
            //  Spawn the gunship on the server so we hold a valid reference
            //  for mayday detection, external destruction, and disconnect cleanup.
            // ----------------------------------------------------------------
            Vector3 playerPos = inventory.PlayerInfo.transform.position;
            GameObject gunshipGo = ServerSpawnGunship(playerPos);

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
            _serverFlyBehaviour = gunshipGo.GetComponent<AC130FlyBehaviour>();
            if (_serverFlyBehaviour != null)
            {
                _serverFlyBehaviour.OnExternallyDestroyed = () =>
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

            // Wire up rocket-hit callbacks.
            var hitReceiver = gunshipGo.GetComponent<AC130HitReceiver>();
            if (hitReceiver != null)
            {
                var gunshipIdentityForDmg = gunshipGo.GetComponent<NetworkIdentity>();
                hitReceiver.OnHit += () =>
                {
                    if (
                        hitReceiver.HitsRequired > 0
                        && hitReceiver.HitCount > 0
                        && hitReceiver.HitCount < hitReceiver.HitsRequired
                    )
                    {
                        IssaPluginPlugin.Log.LogInfo(
                            $"[AC130] Damaged ({hitReceiver.HitCount}/{hitReceiver.HitsRequired}) — broadcasting smoke."
                        );
                        NetworkServer.SendToAll(
                            new AC130DamagedMessage { GunshipNetId = gunshipIdentityForDmg.netId }
                        );
                    }
                };

                hitReceiver.OnHitsExceeded = () =>
                {
                    ServerBeginMayday();
                };
            }

            var gunshipIdentity = gunshipGo.GetComponent<NetworkIdentity>();

            NetworkServer.SendToAll(new AC130SoundMessage());
            ItemWarningBroadcaster.Broadcast(
                inventory.PlayerInfo.PlayerId.PlayerName,
                ItemRegistry.AC130ItemType,
                "AC-130 Gunship",
                trackedNetId: gunshipIdentity.netId,
                senderNetId: netId
            );
            connectionToClient.Send(
                new AC130BeginClientMessage
                {
                    GunshipNetId = gunshipIdentity.netId,
                    OrbitCenter = playerPos,
                }
            );
            _serverTimeout = StartCoroutine(ServerTimeoutRoutine());

            IssaPluginPlugin.Log.LogInfo("[AC130] Server session started.");
        }

        public void ServerEndAC130()
        {
            EndServerSession();
        }

        public void ServerFireAC130(Vector3 aimDirection)
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
            // Offset along the aim direction so the rocket spawns outside the
            // gunship mesh — otherwise it immediately self-collides and explodes.
            Quaternion fireRotation = Quaternion.LookRotation(aimDirection, Vector3.up);
            Vector3 spawnPos = _serverGunship.transform.position + aimDirection * 15f;
            AC130Item.SpawnRocketInDirection(inventory, spawnPos, jitter * fireRotation);
        }

        public void ServerTriggerMayday()
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
        public void ServerPrepareGunshipRocket()
        {
            // No ActiveGunship null-guard here — mirrors ServerPrepareBomberRocket which
            // also sets the flag unconditionally. RocketHomingPatch verifies the
            // AC130GunshipMarker exists in the scene before attaching homing, so if no
            // gunship is present the flag is set but safely ignored at fire time.
            // The old guard (if ActiveGunship == null) return) caused homing to silently
            // fail during fly-out because ReleaseGlobalLock() nulls _activeSessionBridge
            // before the gunship is destroyed.
            PendingGunshipHoming = true;
        }

        /// <summary>
        /// Sent every frame by the owning client while on-station (remote client only).
        /// Applies altitude offset and boost state to the server-side AC130FlyBehaviour.
        /// </summary>
        public void ServerSetFlightInput(float altitudeOffset, bool boosting)
        {
            if (!_serverSessionActive || _serverGunship == null)
                return;

            if (_serverFlyBehaviour == null)
                return;

            _serverFlyBehaviour.altitude = Configuration.AC130Altitude.Value + altitudeOffset;
            _serverFlyBehaviour.orbitSpeed = boosting
                ? Configuration.AC130OrbitSpeed.Value * Configuration.AC130BoostMultiplier.Value
                : Configuration.AC130OrbitSpeed.Value;
        }

        /// <summary>
        /// Sent every frame by the owning client while LocalMaydayActive is true.
        /// Forwards keyboard input so the server-side AC130MaydayBehaviour can apply
        /// player pull and roll to the authoritative dive physics.
        /// diveInfluence: -1 = pull up, +1 = push down.
        /// rollInfluence: -1 = roll left, +1 = roll right.
        /// </summary>
        public void ServerSetMaydayInput(float diveInfluence, float rollInfluence)
        {
            if (!_serverSessionActive || _serverGunship == null)
                return;

            if (_serverMaydayBehaviour == null)
                return;

            _serverMaydayBehaviour.ExternalDiveInfluence = diveInfluence;
            _serverMaydayBehaviour.ExternalRollInfluence = rollInfluence;
        }

        // ================================================================
        //  Server → Client
        // ================================================================

        public void ClientBeginAC130(uint gunshipNetId, Vector3 orbitCenter)
        {
            StartCoroutine(
                RunLocalSession(GetComponent<PlayerInventory>(), gunshipNetId, orbitCenter)
            );
        }

        public void ClientEndAC130()
        {
            _forceEnd = true;
        }

        public void ClientBeginMayday(uint gunshipNetId)
        {
            if (!NetworkClient.spawned.TryGetValue(gunshipNetId, out var ni) || ni == null)
            {
                IssaPluginPlugin.Log.LogError("[Mayday] gunshipIdentity not found in spawned.");
                return;
            }

            _maydayTriggered = true;

            var gunship = ni.gameObject;
            var mayday =
                gunship.GetComponent<AC130MaydayBehaviour>()
                ?? gunship.AddComponent<AC130MaydayBehaviour>();
            mayday.IsLocalPlayer = true;

            // Explicitly initialise the cockpit camera and alarm now that IsLocalPlayer
            // is true. This cannot be done in Start() because on a listen server the
            // component is added by the server before this RPC runs, so Start() fires
            // with IsLocalPlayer=false and skips the camera/alarm setup.
            mayday.BeginAsLocalPlayer();

            // Make the cockpit camera available to overlays (e.g. PlayerBoxOverlay)
            // so they can project world positions correctly during the mayday sequence.
            LocalGunshipCamera = mayday.CockpitCamera;
            LocalMaydayActive = true;
            AC130Overlay.SetMaydayActive(true);

            IssaPluginPlugin.Log.LogInfo("[Mayday] Cockpit cinematic started on owning client.");
        }

        public void ClientEndMayday()
        {
            LocalMaydayActive = false;
            LocalGunshipCamera = null; // cockpit cam is also destroyed with the gunship
            LocalSessionActive = false;
            AC130Overlay.SetMaydayActive(false);
            InputManager.Controls.Gameplay.Enable();
            IssaPluginPlugin.Log.LogInfo("[Mayday] Client mayday ended.");
        }

        public void ClientAC130Busy()
        {
            IssaPluginPlugin.Log.LogInfo("[AC130] AC130 is already in use by another player.");
            // TODO: surface a HUD notification to the player.
        }

        // ================================================================
        //  Local session — runs only on the owning client
        // ================================================================

        private IEnumerator RunLocalSession(
            PlayerInventory inventory,
            uint gunshipNetId,
            Vector3 orbitCenter
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
            float waited = 0f;
            NetworkIdentity gunshipIdentity = null;
            while (
                !NetworkClient.spawned.TryGetValue(gunshipNetId, out gunshipIdentity) && waited < 2f
            )
            {
                waited += Time.deltaTime;
                yield return null;
            }
            if (gunshipIdentity == null)
                IssaPluginPlugin.Log.LogError(
                    "[AC130] Gunship still null after waiting — camera will not activate."
                );
            GameObject gunshipGo = gunshipIdentity?.gameObject;

            var session = new AC130Session(inventory, gunshipGo, orbitCenter);

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
                NetworkClient.Send(new AC130EndMessage());
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

                // On a remote client FlyComp is null, so HandleFlight cannot reach
                // the server-side AC130FlyBehaviour. Forward altitude and boost state
                // when they change, capped at 20 Hz, so the server can apply them
                // authoritatively without generating a packet every frame.
                if (session.FlyComp == null)
                {
                    bool boosting = keyboard != null && keyboard[Key.LeftShift].isPressed;
                    bool flightChanged =
                        Mathf.Abs(session.AltitudeOffset - _lastAltitudeOffset) > 0.05f
                        || boosting != _lastBoosting;

                    _flightInputSendTimer -= Time.deltaTime;

                    if (flightChanged || _flightInputSendTimer <= 0f)
                    {
                        NetworkClient.Send(
                            new AC130FlightInputMessage
                            {
                                AltitudeOffset = session.AltitudeOffset,
                                Boosting = boosting,
                            }
                        );
                        _lastAltitudeOffset = session.AltitudeOffset;
                        _lastBoosting = boosting;
                        _flightInputSendTimer = InputSendInterval;
                    }
                }

                session.GunshipCam?.UpdateLook();

                float currentAngle =
                    session.FlyComp != null
                        ? session.FlyComp.currentAngle
                        : session.Elapsed * session.BaseOrbitSpeed;

                Vector3 gunshipPos = AC130Helpers.OrbitPosition(
                    session.OrbitCenter,
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
                            ItemHelper.GroundLayerMask
                        )
                    )
                        crosshairWorld = hit.point;
                    else
                        crosshairWorld = AC130Item.ProjectAimToGround(camPos, camForward);

                    // Use the actual synced gunship position as the aim origin.
                    // The orbit-math estimate (gunshipPos) can diverge from the real
                    // gunship — especially on remote clients whose angle estimate is
                    // session.Elapsed * BaseOrbitSpeed — causing aimDirection to drift
                    // until it collapses to near-zero and Quaternion.LookRotation(zero)
                    // returns Quaternion.identity, firing rockets straight ahead.
                    // The server always spawns from _serverGunship.transform.position,
                    // so computing the direction from the same reference is correct.
                    Vector3 aimOrigin =
                        session.GunshipVisual != null
                            ? session.GunshipVisual.transform.position
                            : gunshipPos;
                    aimDirection = (crosshairWorld - aimOrigin).normalized;
                }

                AC130Overlay.UpdateAimInfo(crosshairWorld, session.Elapsed, session.Duration);

                bool firePressed = mouse != null && mouse.leftButton.wasPressedThisFrame;
                if (firePressed && session.Cooldown <= 0f)
                {
                    NetworkClient.Send(new AC130FireMessage { AimDirection = aimDirection });
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
            NetworkClient.Send(new AC130EndMessage());

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
                NetworkClient.Send(new AC130TriggerMaydayMessage());
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

        private static GameObject ServerSpawnGunship(Vector3 orbitCenter)
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
                orbitCenter,
                startAngle,
                orbitRadius,
                altitude
            );
            Vector3 approachDir = AC130Helpers.OrbitTangent(startAngle);
            float approachDist = Configuration.AC130ApproachDistance.Value;
            float approachSpeed = Configuration.AC130ApproachSpeed.Value;

            Vector3 spawnPos = orbitEntry - approachDir * approachDist;

            var ac130GameObj = Object.Instantiate(
                AssetLoader.AC130Prefab,
                spawnPos,
                Quaternion.LookRotation(approachDir, Vector3.up)
            );

            // NetworkIdentity (with stable assetId) is baked into AC130Prefab in AssetLoader.
            // AC130ClientSetup.Awake() (on the prefab) already adds Entity, LockOnTarget,
            // and AC130GunshipMarker during Instantiate — add runtime-only components after.

            // AC130HitReceiver (CustomHittable) needs Entity in its Awake;
            // Entity was already added by AC130ClientSetup.Awake() during Instantiate above.
            var hitReceiver = ac130GameObj.AddComponent<AC130HitReceiver>();

            var flyComp = ac130GameObj.AddComponent<AC130FlyBehaviour>();
            flyComp.orbitCenter = orbitCenter;
            flyComp.orbitRadius = orbitRadius;
            flyComp.altitude = altitude;
            flyComp.orbitSpeed = Configuration.AC130OrbitSpeed.Value;
            flyComp.currentAngle = startAngle;
            flyComp.flyTarget = orbitEntry;
            flyComp.flySpeed = approachSpeed;
            flyComp.mode = AC130FlightMode.FlyIn;

            // Spawn AFTER all setup so Start() fires post-Spawn, not pre-Spawn.
            NetworkServer.Spawn(ac130GameObj);

            IssaPluginPlugin.Log.LogInfo(
                $"[AC130] Gunship spawned at approach distance {approachDist:F0}m."
            );

            return ac130GameObj;
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

            if (_serverGunship == null)
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
            // Capture orbitCenter BEFORE destroying the fly component.
            Vector3 orbitCenter =
                _serverFlyBehaviour != null
                    ? _serverFlyBehaviour.orbitCenter
                    : _serverGunship.transform.position;
            if (_serverFlyBehaviour != null)
            {
                _serverFlyBehaviour.OnExternallyDestroyed = null; // prevent re-entry
                Object.Destroy(_serverFlyBehaviour);
                _serverFlyBehaviour = null;
            }

            var gunshipIdentity = _serverGunship.GetComponent<NetworkIdentity>();

            // Add the authoritative mayday driver on the server.
            _serverMaydayBehaviour = _serverGunship.AddComponent<AC130MaydayBehaviour>();
            _serverMaydayBehaviour.IsLocalPlayer = false;
            _serverMaydayBehaviour.OrbitCenter = orbitCenter;
            _serverMaydayBehaviour.OnImpact = () =>
                ServerHandleMaydayImpact(_serverGunship.transform.position);

            // Owning client gets the cockpit camera.
            if (_serverSessionActive)
            {
                connectionToClient.Send(
                    new AC130BeginMaydayClientMessage { GunshipNetId = gunshipIdentity.netId }
                );
            }

            // All other clients get smoke trail.
            NetworkServer.SendToAll(
                new AC130MaydayVfxMessage { GunshipNetId = gunshipIdentity.netId }
            );

            IssaPluginPlugin.Log.LogInfo("[AC130] Server mayday sequence started.");
        }

        private void ServerHandleMaydayImpact(Vector3 impactPos)
        {
            IssaPluginPlugin.Log.LogInfo($"[Mayday] Impact at {impactPos}.");

            // VFX, screen shake, and audio on all clients via NetworkMessage.
            NetworkServer.SendToAll(new AC130MaydayImpactMessage { ImpactPos = impactPos });

            ServerSpawnImpactRocket(impactPos);

            if (_serverGunship != null)
            {
                Object.Destroy(_serverGunship);
                _serverGunship = null;
            }

            _serverMaydayBehaviour = null;
            _serverSessionActive = false;
            ReleaseGlobalLock();
            connectionToClient.Send(new AC130EndMaydayClientMessage());
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
            {
                IssaPluginPlugin.Log.LogError(
                    "[AC130NetworkBridge] Rocket prefab failed to instantiate."
                );
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
            connectionToClient.Send(new AC130EndClientMessage());

            // Begin fly-out instead of immediately destroying the gunship.
            // AC130FlyBehaviour.UpdateFlyOut() will call Object.Destroy once it
            // travels FlyOutDestroyDistance — Mirror propagates that to all clients.
            // Clear the destruction callback first so the normal fly-out doesn't
            // accidentally trigger a mayday.
            if (_serverFlyBehaviour != null)
            {
                _serverFlyBehaviour.OnExternallyDestroyed = null;
                _serverFlyBehaviour.BeginFlyOut();
                // Do NOT null _serverFlyBehaviour here — if a rocket hits during fly-out
                // ServerBeginMayday still needs orbitCenter from it. The field becomes
                // fake-null when the gunship self-destructs, or is nulled by ServerBeginMayday
                // itself (line 793) or ForceServerCleanup.
            }
            else if (_serverGunship != null)
            {
                // No fly component (prefab not loaded or already removed) — fall back.
                NetworkServer.Destroy(_serverGunship);
                _serverGunship = null;
            }

            // Do NOT null _serverGunship here. Keep the reference alive so that
            // ServerBeginMayday can still fire if a rocket hits during fly-out.
            // Unity's fake-null makes _serverGunship == null automatically once
            // the gameObject is destroyed at the end of the fly-out path.
            IssaPluginPlugin.Log.LogInfo("[AC130] Server session ended — gunship flying out.");
        }

        public override void ServerHoleCleanup()
        {
            if (_serverSessionActive)
                ForceServerCleanup();
        }

        public override void ClientHoleCleanup() // Runs on client
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

            if (_serverGunship != null)
            {
                NetworkServer.Destroy(_serverGunship);
                _serverGunship = null;
            }

            _serverFlyBehaviour = null;
            _serverMaydayBehaviour = null;
            _serverSessionActive = false;
            ReleaseGlobalLock();
        }

        private static void ReleaseGlobalLock()
        {
            _globalSessionActive = false;
            _activeSessionBridge = null;
        }

        public static void ForceReleaseGlobalLock()
        {
            IssaPluginPlugin.Log.LogWarning(
                "[AC130] ForceReleaseGlobalLock called — resetting session state."
            );
            _globalSessionActive = false;
            _activeSessionBridge = null;
        }

        // ── NetworkMessage handlers (registered by NetworkManagerPatches) ─────

        internal static void HandleAC130Sound(AC130SoundMessage msg)
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

        internal static void HandleAC130MaydayVfx(AC130MaydayVfxMessage msg)
        {
            // Skip for the owning client — TargetBeginMayday handles the cockpit path.
            // All other clients get the external smoke/fire mayday behaviour here.
            var localBridge = NetworkClient.localPlayer?.GetComponent<AC130NetworkBridge>();
            if (localBridge != null && localBridge.LocalSessionActive)
                return;

            if (!NetworkClient.spawned.TryGetValue(msg.GunshipNetId, out var ni) || ni == null)
                return;

            var gunship = ni.gameObject;
            if (gunship.GetComponent<AC130MaydayBehaviour>() == null)
            {
                var mayday = gunship.AddComponent<AC130MaydayBehaviour>();
                mayday.IsLocalPlayer = false;
                mayday.OrbitCenter =
                    gunship.GetComponent<AC130FlyBehaviour>()?.orbitCenter ?? Vector3.zero;
            }
        }

        internal static void HandleAC130Damaged(AC130DamagedMessage msg)
        {
            if (AssetLoader.MaydaySmokeTrailPrefab == null)
                return;

            if (!NetworkClient.spawned.TryGetValue(msg.GunshipNetId, out var ni) || ni == null)
                return;

            IssaPluginPlugin.Log.LogInfo("[AC130] Spawning damage smoke trail.");
            var smoke = Instantiate(
                AssetLoader.MaydaySmokeTrailPrefab,
                ni.transform.position,
                Quaternion.identity
            );
            smoke.transform.SetParent(ni.transform, worldPositionStays: true);
        }

        internal static void HandleAC130MaydayImpact(AC130MaydayImpactMessage msg)
        {
            float duration = Configuration.AC130MaydayExplosionDuration.Value;

            if (AssetLoader.MaydayExplosionVfxPrefab != null)
            {
                var vfxGo = Instantiate(
                    AssetLoader.MaydayExplosionVfxPrefab,
                    msg.ImpactPos,
                    Quaternion.identity
                );
                Destroy(vfxGo, duration);
            }
            else
            {
                VfxManager.PlayPooledVfxLocalOnly(
                    VfxType.RocketLauncherRocketExplosion,
                    msg.ImpactPos,
                    Quaternion.identity,
                    Vector3.one * Configuration.AC130MaydayExplosionScale.Value
                );
            }

            if (AssetLoader.ImpactVfxPrefab != null)
            {
                var debrisGo = Instantiate(
                    AssetLoader.ImpactVfxPrefab,
                    msg.ImpactPos,
                    Quaternion.identity
                );
                Destroy(debrisGo, duration);
            }

            CameraModuleController.Shake(
                GameManager.CameraGameplaySettings.RocketExplosionScreenshakeSettings,
                msg.ImpactPos
            );
        }
    }
}
