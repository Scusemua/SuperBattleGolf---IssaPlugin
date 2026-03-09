using System.Collections;
using Mirror;
using UnityEngine;
using UnityEngine.InputSystem;
using IssaPlugin.Overlays;

namespace IssaPlugin.Items
{
    /// Attached to the player object via NetworkBridgePatches.
    /// Handles AC130 activation, fire Commands, and session lifecycle RPCs.
    ///
    /// All mutable session state lives here as instance fields (one bridge
    /// per player NetworkObject) rather than in static fields on AC130Item,
    /// so multiple players can each run an independent AC130 session at the
    /// same time without interfering with each other.
    public class AC130NetworkBridge : NetworkBehaviour
    {
        // ================================================================
        //  Per-instance server state
        // ================================================================

        private Coroutine _serverTimeout;

        /// True on the server while this player's session is running.
        private bool _serverSessionActive;

        // ================================================================
        //  Per-instance client state  (meaningful only on the owning client)
        // ================================================================

        /// True on the local client while this player's session coroutine
        /// is running. Read by MissileOverlay to decide which camera to use.
        public bool LocalSessionActive { get; private set; }

        /// Set to true to break the coroutine loop from outside.
        private bool _forceEnd;

        /// The active gunship Camera while this player's local session runs,
        /// null otherwise. Read by MissileOverlay for WorldToScreenPoint.
        public Camera LocalGunshipCamera { get; private set; }

        // ================================================================
        //  Client → Server
        // ================================================================

        [Command]
        public void CmdStartAC130()
        {
            // Guard is per-instance — each player has their own bridge, so
            // one player's active session doesn't block another's.
            if (_serverSessionActive)
            {
                IssaPluginPlugin.Log.LogWarning("[AC130] Session already active for this player.");
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
            _serverSessionActive = true;
            RpcPlayAC130Sound();
            TargetBeginAC130(connectionToClient);
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
            // Guard against stale fire commands arriving after session ended.
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

        // ================================================================
        //  Server → Client
        // ================================================================

        [TargetRpc]
        public void TargetBeginAC130(NetworkConnection target)
        {
            StartCoroutine(RunLocalSession(GetComponent<PlayerInventory>()));
        }

        [TargetRpc]
        public void TargetEndAC130(NetworkConnection target)
        {
            // Set the flag on this bridge instance, not a static field, so
            // only this player's session coroutine is interrupted.
            _forceEnd = true;
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

            // Use a 2D AudioSource (spatialBlend = 0) so the clip is always
            // audible regardless of AudioListener position.
            var go = new GameObject("AC130_Sound");
            var src = go.AddComponent<AudioSource>();
            src.clip = clip;
            src.spatialBlend = 0f;
            src.volume = 1f;
            src.Play();

            Destroy(go, clip.length + 0.1f);
            IssaPluginPlugin.Log.LogInfo("[AC130] Playing ac130_above sound.");
        }

        // ================================================================
        //  Local session — runs only on the owning client
        // ================================================================

        private IEnumerator RunLocalSession(PlayerInventory inventory)
        {
            LocalSessionActive = true;
            _forceEnd = false;

            InputManager.Controls.Gameplay.Disable();

            var session = new AC130Session(inventory, inventory.PlayerInfo.transform.position);

            // ============================================================
            //  Phase 1: Fly-in — OrbitModule follows the visual cinematically
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

                while (!session.FlyComp.HasArrived && !_forceEnd)
                {
                    if (Keyboard.current != null && Keyboard.current[Key.Space].wasPressedThisFrame)
                    {
                        _forceEnd = true;
                        break;
                    }

                    session.PivotGo.transform.position = session.GunshipVisual.transform.position;
                    session.OrbitModule?.ForceUpdateModule();

                    yield return null;
                }

                IssaPluginPlugin.Log.LogInfo("[AC130] Fly-in complete.");
            }

            if (_forceEnd)
            {
                session.Cleanup();
                LocalGunshipCamera = null;
                LocalSessionActive = false;
                CmdEndAC130();
                yield break;
            }

            // ============================================================
            //  Phase 2: On-station — gunship camera, mouse-look, firing
            // ============================================================
            session.BeginGunshipView();
            LocalGunshipCamera = session.GunshipCam?.Camera;

            while (session.Elapsed < session.Duration && !_forceEnd)
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

                // Crosshair is always screen centre — raycast along camera forward.
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

            // ============================================================
            //  Phase 3: Fly-out
            // ============================================================
            session.Cleanup();
            LocalGunshipCamera = null;
            LocalSessionActive = false;
            CmdEndAC130();

            IssaPluginPlugin.Log.LogInfo("[AC130] Session ended, gunship flying out.");
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
            TargetEndAC130(connectionToClient);
            IssaPluginPlugin.Log.LogInfo("[AC130] Server session ended.");
        }
    }
}
