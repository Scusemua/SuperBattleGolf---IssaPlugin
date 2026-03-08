using System.Collections;
using System.Reflection;
using Mirror;
using UnityEngine;
using UnityEngine.InputSystem;

namespace IssaPlugin.Items
{
    public static class AC130Item
    {
        public static readonly ItemType AC130ItemType = (ItemType)103;

        private static bool _isActive;

        private static int _useIndex;

        public static bool IsActive => _isActive;
        public static Vector3 GunshipPosition { get; private set; }
        public static Vector3 GunshipFacing { get; private set; }

        public static void GiveAC130ToLocalPlayer()
        {
            ItemHelper.GiveItemToLocalPlayer(AC130ItemType, Configuration.AC130Uses.Value, "AC130");
        }

        private static void SetCurrentItemUse(PlayerInventory inventory, ItemUseType itemUseType)
        {
            var method = typeof(PlayerInventory).GetMethod(
                "SetCurrentItemUse",
                BindingFlags.NonPublic | BindingFlags.Instance
            );

            if (method != null)
                method.Invoke(inventory, new object[] { itemUseType });
            else
                IssaPluginPlugin.Log.LogError("[AC130] Could not find SetCurrentItemUse method.");
        }

        // ----------------------------------------------------------------
        //  Entry point
        // ----------------------------------------------------------------

        public static IEnumerator AC130Routine(PlayerInventory inventory)
        {
            if (_isActive)
                yield break;

            _isActive = true;

            AC130NetworkBridge.ServerSpawn(inventory);

            int equippedIndex = inventory.EquippedItemIndex;

            if (NetworkServer.active)
            {
                SetCurrentItemUse(inventory, ItemUseType.Regular);
                if (equippedIndex >= 0)
                    ItemHelper.DecrementAndRemove(inventory, equippedIndex);
                SetCurrentItemUse(inventory, ItemUseType.None);
            }

            yield return new WaitForSeconds(0.01f);

            var session = new AC130Session(inventory, GetMapCentre(inventory));
            yield return RunAC130Session(inventory, session);

            session.Cleanup();
            AC130NetworkBridge.ServerDespawn();
            _isActive = false;
        }

        public static IEnumerator AC130ClientRoutine(PlayerInventory inventory)
        {
            // Handles local-only effects: overlay, FOV zoom, input forwarding.
            InputManager.Controls.Gameplay.Disable();

            OrbitCameraModule orbitModule = null;
            CameraModuleController.TryGetOrbitModule(out orbitModule);

            float savedPitch = orbitModule?.Pitch ?? 0f;
            float savedYaw = orbitModule?.Yaw ?? 0f;
            bool savedDisablePhysics = orbitModule?.disablePhysics ?? false;

            float elapsed = 0f;
            float duration = Configuration.AC130Duration.Value;
            float aimYaw = 0f;
            float aimPitch = Configuration.AC130AimPitchDefault.Value;
            float originalFov = Camera.main != null ? Camera.main.fieldOfView : 60f;
            float currentFov = originalFov;

            while (elapsed < duration && _isActive)
            {
                elapsed += Time.deltaTime;

                var keyboard = Keyboard.current;
                var mouse = Mouse.current;

                if (keyboard != null && keyboard[Key.Space].wasPressedThisFrame)
                    break;

                // These are local-only session objects so we reconstruct aim state here.
                HandleAC130Aim(
                    keyboard,
                    ref aimYaw,
                    ref aimPitch,
                    Configuration.AC130AimYawSpeed.Value,
                    Configuration.AC130AimPitchSpeed.Value,
                    Configuration.AC130AimPitchMin.Value,
                    Configuration.AC130AimPitchMax.Value,
                    Configuration.AC130AimYawMax.Value
                );

                HandleAC130Zoom(mouse, ref currentFov, originalFov);

                // Derive gunship position from orbit math using the shared static property.
                Vector3 gunshipPos = GunshipPosition;
                Vector3 gunshipFacing = GunshipFacing;

                Quaternion aimRotation =
                    Quaternion.LookRotation(gunshipFacing, Vector3.up)
                    * Quaternion.Euler(aimPitch, aimYaw, 0f);
                Vector3 aimDirection = aimRotation * Vector3.down;
                Vector3 crosshair = ProjectAimToGround(gunshipPos, aimDirection);

                AC130Overlay.UpdateAimInfo(crosshair, elapsed, duration);

                // Send fire command to server via a Command rather than spawning directly.
                bool firePressed =
                    (keyboard != null && keyboard[Key.F].wasPressedThisFrame)
                    || (mouse != null && mouse.leftButton.wasPressedThisFrame);

                if (firePressed)
                    AC130NetworkBridge.CmdRequestFire(inventory, gunshipPos, aimDirection);

                yield return null;
            }

            if (Camera.main != null)
                Camera.main.fieldOfView = originalFov;

            RestoreCamera(orbitModule, savedPitch, savedYaw, savedDisablePhysics);
            InputManager.Controls.Gameplay.Enable();
        }

        private static IEnumerator RunAC130Session(PlayerInventory inventory, AC130Session s)
        {
            InputManager.Controls.Gameplay.Disable();

            while (s.Elapsed < s.Duration)
            {
                s.Elapsed += Time.deltaTime;
                s.Cooldown -= Time.deltaTime;

                var keyboard = Keyboard.current;
                var mouse = Mouse.current;

                if (keyboard != null && keyboard[Key.Space].wasPressedThisFrame)
                {
                    IssaPluginPlugin.Log.LogInfo("[AC130] Player exited early.");
                    break;
                }

                HandleAC130Flight(keyboard, s);
                HandleAC130Aim(
                    keyboard,
                    ref s.AimYaw,
                    ref s.AimPitch,
                    s.AimYawSpeed,
                    s.AimPitchSpeed,
                    s.AimPitchMin,
                    s.AimPitchMax,
                    s.AimYawMax
                );
                HandleAC130Zoom(mouse, ref s.CurrentFov, s.OriginalFov);

                Vector3 gunshipPos =
                    s.GunshipVisual != null
                        ? s.GunshipVisual.transform.position
                        : AC130Helpers.OrbitPosition(
                            s.MapCentre,
                            s.Elapsed * s.BaseOrbitSpeed,
                            s.OrbitRadius,
                            s.Altitude + s.AltitudeOffset
                        );
                Vector3 gunshipFacing =
                    s.GunshipVisual != null
                        ? s.GunshipVisual.transform.forward
                        : AC130Helpers.OrbitTangent(s.Elapsed * s.BaseOrbitSpeed);

                GunshipPosition = gunshipPos;
                GunshipFacing = gunshipFacing;
                s.PivotGo.transform.position = gunshipPos;
                s.OrbitModule?.ForceUpdateModule();

                // In RunAC130Session, replace the aimRotation + crosshairWorld block with:
                Quaternion aimRotation =
                    Quaternion.LookRotation(gunshipFacing, Vector3.up)
                    * Quaternion.Euler(s.AimPitch, s.AimYaw, 0f);
                Vector3 aimDirection = aimRotation * Vector3.down; // aim DOWN, offset by pitch/yaw
                Vector3 crosshairWorld = ProjectAimToGround(gunshipPos, aimDirection);

                AC130Overlay.UpdateAimInfo(crosshairWorld, s.Elapsed, s.Duration);
                HandleAC130Fire(
                    inventory,
                    mouse,
                    keyboard,
                    gunshipPos,
                    aimDirection,
                    crosshairWorld,
                    s
                );

                yield return null;
            }

            IssaPluginPlugin.Log.LogInfo("[AC130] Run ended.");
        }

        private static void HandleAC130Zoom(Mouse mouse, ref float currentFov, float originalFov)
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

        private static void HandleAC130Flight(Keyboard keyboard, AC130Session s)
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

        private static void HandleAC130Aim(
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

        private static void HandleAC130Fire(
            PlayerInventory inventory,
            Mouse mouse,
            Keyboard keyboard,
            Vector3 gunshipPos,
            Vector3 aimDirection,
            Vector3 crosshairWorld,
            AC130Session s
        )
        {
            bool firePressed =
                (keyboard != null && keyboard[Key.F].wasPressedThisFrame)
                || (mouse != null && mouse.leftButton.wasPressedThisFrame);

            if (!firePressed || s.Cooldown > 0f)
                return;

            float angularJitter = Configuration.AC130RocketAngularJitter.Value;
            Quaternion jitter = Quaternion.Euler(
                Random.Range(-angularJitter, angularJitter),
                Random.Range(-angularJitter, angularJitter),
                0f
            );

            // Build rotation from the resolved world-space aim direction.
            Quaternion fireRotation = Quaternion.LookRotation(aimDirection, Vector3.up);
            SpawnRocketInDirection(inventory, gunshipPos, jitter * fireRotation);
            IssaPluginPlugin.Log.LogInfo($"[AC130] Rocket fired toward {crosshairWorld}.");
            s.Cooldown = s.FireCooldown;
        }

        private static Vector3 ProjectAimToGround(Vector3 origin, Vector3 direction)
        {
            // Intersect the aim ray with y = 0 (sea-level ground plane).
            if (Mathf.Abs(direction.y) < 0.001f)
                return origin + direction * 500f;

            float t = -origin.y / direction.y;
            if (t < 0f)
                return origin + direction * 500f;

            return origin + direction * t;
        }

        private static void SpawnRocket(
            PlayerInventory inventory,
            Vector3 position,
            Quaternion rotation
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
                rotation
            );

            if (rocket == null)
            {
                IssaPluginPlugin.Log.LogError("[AC130] Rocket did not instantiate.");
                return;
            }

            rocket.ServerInitialize(inventory.PlayerInfo, null, itemUseId);
            NetworkServer.Spawn(rocket.gameObject, (NetworkConnectionToClient)null);
        }

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

        private static void RestoreCamera(
            OrbitCameraModule orbitModule,
            float savedPitch,
            float savedYaw,
            bool savedDisablePhysics
        )
        {
            if (orbitModule == null)
                return;

            var playerMovement = GameManager.LocalPlayerMovement;
            if (playerMovement != null)
                orbitModule.SetSubject(playerMovement.transform);

            orbitModule.SetDistanceAddition(0f);
            orbitModule.disablePhysics = savedDisablePhysics;
            orbitModule.SetPitch(savedPitch);
            orbitModule.SetYaw(savedYaw);
            orbitModule.ForceUpdateModule();
        }

        private static Vector3 GetMapCentre(PlayerInventory inventory)
        {
            // Use the player's current position projected to ground as the
            // orbit centre so the gunship always circles the action.
            var pos = inventory.PlayerInfo.transform.position;
            return new Vector3(pos.x, 0f, pos.z);
        }
    }
}
