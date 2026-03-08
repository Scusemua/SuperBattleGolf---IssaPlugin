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
                HandleAim(
                    keyboard,
                    ref session.AimYaw,
                    ref session.AimPitch,
                    session.AimYawSpeed,
                    session.AimPitchSpeed,
                    session.AimPitchMin,
                    session.AimPitchMax,
                    session.AimYawMax
                );
                HandleZoom(mouse, ref session.CurrentFov, session.OriginalFov);

                Vector3 gunshipPos =
                    session.GunshipVisual != null
                        ? session.GunshipVisual.transform.position
                        : AC130Helpers.OrbitPosition(
                            session.MapCentre,
                            session.Elapsed * session.BaseOrbitSpeed,
                            session.OrbitRadius,
                            session.Altitude + session.AltitudeOffset
                        );

                Vector3 gunshipFacing =
                    session.GunshipVisual != null
                        ? session.GunshipVisual.transform.forward
                        : AC130Helpers.OrbitTangent(session.Elapsed * session.BaseOrbitSpeed);

                GunshipPosition = gunshipPos;
                GunshipFacing = gunshipFacing;
                session.PivotGo.transform.position = gunshipPos;
                session.OrbitModule?.ForceUpdateModule();

                Quaternion aimRotation =
                    Quaternion.LookRotation(gunshipFacing, Vector3.up)
                    * Quaternion.Euler(session.AimPitch, session.AimYaw, 0f);
                Vector3 aimDirection = aimRotation * Vector3.down;
                Vector3 crosshairWorld = ProjectAimToGround(gunshipPos, aimDirection);

                AC130Overlay.UpdateAimInfo(crosshairWorld, session.Elapsed, session.Duration);

                bool firePressed =
                    (keyboard != null && keyboard[Key.F].wasPressedThisFrame)
                    || (mouse != null && mouse.leftButton.wasPressedThisFrame);

                if (firePressed && session.Cooldown <= 0f)
                {
                    bridge.CmdFireAC130(gunshipPos, aimDirection);
                    session.Cooldown = session.FireCooldown;
                    IssaPluginPlugin.Log.LogInfo($"[AC130] Rocket fired toward {crosshairWorld}.");
                }

                yield return null;
            }

            session.Cleanup();
            bridge.CmdEndAC130();
            _isActive = false;
            _forceEnd = false;

            IssaPluginPlugin.Log.LogInfo("[AC130] Local session ended.");
        }

        // ----------------------------------------------------------------
        //  Input handlers
        // ----------------------------------------------------------------

        private static void HandleZoom(Mouse mouse, ref float currentFov, float originalFov)
        {
            if (mouse == null)
                return;

            float targetFov = mouse.rightButton.isPressed
                ? Configuration.AC130ZoomFov.Value
                : originalFov;

            currentFov = Mathf.Lerp(
                currentFov,
                targetFov,
                Configuration.AC130ZoomSpeed.Value * Time.deltaTime
            );

            if (Camera.main != null)
                Camera.main.fieldOfView = currentFov;
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

        private static void HandleAim(
            Keyboard keyboard,
            ref float aimYaw,
            ref float aimPitch,
            float yawSpeed,
            float pitchSpeed,
            float pitchMin,
            float pitchMax,
            float yawMax
        )
        {
            if (keyboard == null)
                return;

            if (keyboard[Key.A].isPressed || keyboard[Key.LeftArrow].isPressed)
                aimYaw -= yawSpeed * Time.deltaTime;
            if (keyboard[Key.D].isPressed || keyboard[Key.RightArrow].isPressed)
                aimYaw += yawSpeed * Time.deltaTime;
            if (keyboard[Key.W].isPressed || keyboard[Key.UpArrow].isPressed)
                aimPitch = Mathf.Clamp(aimPitch - pitchSpeed * Time.deltaTime, pitchMin, pitchMax);
            if (keyboard[Key.S].isPressed || keyboard[Key.DownArrow].isPressed)
                aimPitch = Mathf.Clamp(aimPitch + pitchSpeed * Time.deltaTime, pitchMin, pitchMax);

            aimYaw = Mathf.Clamp(aimYaw, -yawMax, yawMax);
        }

        // ----------------------------------------------------------------
        //  Server-side rocket spawning (called from AC130NetworkBridge)
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
            var pos = inventory.PlayerInfo.transform.position;
            return new Vector3(pos.x, 0f, pos.z);
        }
    }
}
