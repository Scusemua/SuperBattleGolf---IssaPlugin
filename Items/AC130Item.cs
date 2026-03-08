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
                HandleZoom(mouse, ref session.CurrentFov, session.OriginalFov);

                // Use the FlyComp's current orbit angle for horizontal position
                // but the TARGET altitude (not the lerped visual altitude) so the
                // camera responds instantly to Q/E input instead of lagging.
                float currentAngle = session.FlyComp != null
                    ? session.FlyComp.currentAngle
                    : session.Elapsed * session.BaseOrbitSpeed;

                Vector3 gunshipPos = AC130Helpers.OrbitPosition(
                    session.MapCentre,
                    currentAngle,
                    session.OrbitRadius,
                    session.Altitude + session.AltitudeOffset
                );

                Vector3 gunshipFacing = AC130Helpers.OrbitTangent(currentAngle);

                GunshipPosition = gunshipPos;
                GunshipFacing = gunshipFacing;
                session.PivotGo.transform.position = gunshipPos;
                session.OrbitModule?.ForceUpdateModule();

                // Mouse-based aiming: raycast from camera through mouse cursor
                Vector3 crosshairWorld = gunshipPos;
                Vector3 aimDirection = Vector3.down;

                if (Camera.main != null && mouse != null)
                {
                    Ray aimRay = Camera.main.ScreenPointToRay(mouse.position.ReadValue());

                    if (Physics.Raycast(aimRay, out RaycastHit hit, 5000f, GroundLayerMask))
                        crosshairWorld = hit.point;
                    else
                        crosshairWorld = ProjectAimToGround(aimRay.origin, aimRay.direction);

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
            var pos = inventory.PlayerInfo.transform.position;
            return new Vector3(pos.x, 0f, pos.z);
        }
    }
}
