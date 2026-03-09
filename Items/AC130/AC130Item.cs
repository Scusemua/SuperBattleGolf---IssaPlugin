using System.Collections;
using Mirror;
using UnityEngine;
using UnityEngine.InputSystem;

namespace IssaPlugin.Items
{
    public static class AC130Item
    {
        public static readonly ItemType AC130ItemType = (ItemType)103;

        private static bool _isActive;
        private static bool _forceEnd;
        private static int _useIndex;

        public static bool IsActive => _isActive;
        public static Vector3 GunshipPosition { get; private set; }
        public static Vector3 GunshipFacing { get; private set; }

        private static readonly int GroundLayerMask = LayerMask.GetMask("Default", "Terrain");

        public static void GiveAC130ToLocalPlayer()
        {
            ItemHelper.GiveItemToLocalPlayer(AC130ItemType, Configuration.AC130Uses.Value, "AC130");
        }

        public static void SetActive(bool active) => _isActive = active;

        public static void ForceEndLocalSession() => _forceEnd = true;

        // ----------------------------------------------------------------
        //  Local session — runs on the controlling player's client
        // ----------------------------------------------------------------

        public static IEnumerator RunLocalSession(
            PlayerInventory inventory,
            AC130NetworkBridge bridge
        )
        {
            _isActive = true;
            _forceEnd = false;

            InputManager.Controls.Gameplay.Disable();

            var session = new AC130Session(inventory, GetMapCentre(inventory));

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

                    var visualPos = session.GunshipVisual.transform.position;
                    session.PivotGo.transform.position = visualPos;
                    GunshipPosition = visualPos;
                    GunshipFacing = session.GunshipVisual.transform.forward;
                    session.OrbitModule?.ForceUpdateModule();

                    yield return null;
                }

                IssaPluginPlugin.Log.LogInfo("[AC130] Fly-in complete.");
            }

            if (_forceEnd)
            {
                session.Cleanup();
                bridge.CmdEndAC130();
                _isActive = false;
                _forceEnd = false;
                yield break;
            }

            // ============================================================
            //  Phase 2: On-station — gunship camera, mouse-look, firing
            // ============================================================
            session.BeginGunshipView();

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

                HandleFlight(keyboard, session);

                // Let the camera consume mouse delta and update its look offset.
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

                GunshipPosition = gunshipPos;
                GunshipFacing = AC130Helpers.OrbitTangent(currentAngle);

                HandleZoom(mouse, session);

                // --------------------------------------------------------
                //  Aim — crosshair is always screen centre, so we raycast
                //  along the camera's forward vector rather than using
                //  the mouse cursor position.
                // --------------------------------------------------------
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
                            GroundLayerMask
                        )
                    )
                        crosshairWorld = hit.point;
                    else
                        crosshairWorld = ProjectAimToGround(camPos, camForward);

                    aimDirection = (crosshairWorld - gunshipPos).normalized;
                }

                AC130Overlay.UpdateAimInfo(crosshairWorld, session.Elapsed, session.Duration);

                bool firePressed = mouse != null && mouse.leftButton.wasPressedThisFrame;

                if (firePressed && session.Cooldown <= 0f)
                {
                    bridge.CmdFireAC130(gunshipPos, aimDirection);
                    session.Cooldown = session.FireCooldown;
                    IssaPluginPlugin.Log.LogInfo($"[AC130] Rocket fired toward {crosshairWorld}.");
                }

                yield return null;
            }

            // ============================================================
            //  Phase 3: Fly-out
            // ============================================================
            session.Cleanup();
            bridge.CmdEndAC130();
            _isActive = false;
            _forceEnd = false;

            IssaPluginPlugin.Log.LogInfo("[AC130] Session ended, gunship flying out.");
        }

        // ----------------------------------------------------------------
        //  Input handlers
        // ----------------------------------------------------------------

        private static void HandleZoom(Mouse mouse, AC130Session session)
        {
            if (mouse == null || session.GunshipCam == null)
                return;

            float targetFov = mouse.rightButton.isPressed
                ? Configuration.AC130ZoomFov.Value
                : session.GunshipCam.baseFov;

            session.GunshipCam.SetFov(targetFov, Configuration.AC130ZoomSpeed.Value);
        }

        private static void HandleFlight(Keyboard keyboard, AC130Session s)
        {
            bool boosting = keyboard != null && keyboard[Key.LeftShift].isPressed;
            if (s.FlyComp != null)
                s.FlyComp.orbitSpeed = boosting ? s.BoostedOrbitSpeed : s.BaseOrbitSpeed;

            if (keyboard == null)
                return;

            if (keyboard[Key.Q].isPressed)
                s.AltitudeOffset -= s.AltitudeAdjustSpeed * Time.deltaTime;
            if (keyboard[Key.E].isPressed)
                s.AltitudeOffset += s.AltitudeAdjustSpeed * Time.deltaTime;

            s.AltitudeOffset = Mathf.Clamp(
                s.AltitudeOffset,
                -s.AltitudeOffsetMax,
                s.AltitudeOffsetMax
            );

            if (s.FlyComp != null)
                s.FlyComp.altitude = s.Altitude + s.AltitudeOffset;
        }

        // ----------------------------------------------------------------
        //  Server-side rocket spawning
        // ----------------------------------------------------------------

        public static void SpawnRocketInDirection(
            PlayerInventory inventory,
            Vector3 position,
            Quaternion worldRotation
        )
        {
            if (!NetworkServer.active)
                return;

            _useIndex++;
            var itemUseId = new ItemUseId(
                inventory.PlayerInfo.PlayerId.Guid,
                _useIndex,
                ItemType.RocketLauncher
            );

            var rocket = Object.Instantiate(
                GameManager.ItemSettings.RocketPrefab,
                position,
                worldRotation
            );

            if (rocket == null)
            {
                IssaPluginPlugin.Log.LogError("[AC130] Rocket did not instantiate.");
                return;
            }

            rocket.ServerInitialize(inventory.PlayerInfo, null, itemUseId);
            NetworkServer.Spawn(rocket.gameObject, (NetworkConnectionToClient)null);

            ExplosionScaler.Register(rocket, Configuration.AC130ExplosionScale.Value);
        }

        // ----------------------------------------------------------------
        //  Helpers
        // ----------------------------------------------------------------

        private static Vector3 ProjectAimToGround(Vector3 origin, Vector3 direction)
        {
            if (Mathf.Abs(direction.y) < 0.001f)
                return origin + direction * 500f;

            float t = -origin.y / direction.y;
            if (t < 0f)
                return origin + direction * 500f;

            return origin + direction * t;
        }

        private static Vector3 GetMapCentre(PlayerInventory inventory)
        {
            return inventory.PlayerInfo.transform.position;
        }
    }
}
