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
            _isActive = false;
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
                HandleAC130Aim(keyboard, s);
                HandleAC130Zoom(mouse, s);

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

        private static void HandleAC130Zoom(Mouse mouse, AC130Session s)
        {
            if (mouse == null)
                return;

            float zoomFov = Configuration.AC130ZoomFov.Value;
            float zoomSpeed = Configuration.AC130ZoomSpeed.Value;
            float targetFov = mouse.rightButton.isPressed ? zoomFov : s.OriginalFov;

            s.CurrentFov = Mathf.Lerp(s.CurrentFov, targetFov, zoomSpeed * Time.deltaTime);

            if (Camera.main != null)
                Camera.main.fieldOfView = s.CurrentFov;
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

        private static void HandleAC130Aim(Keyboard keyboard, AC130Session s)
        {
            if (keyboard == null)
                return;

            if (keyboard[Key.A].isPressed || keyboard[Key.LeftArrow].isPressed)
                s.AimYaw -= s.AimYawSpeed * Time.deltaTime;
            if (keyboard[Key.D].isPressed || keyboard[Key.RightArrow].isPressed)
                s.AimYaw += s.AimYawSpeed * Time.deltaTime;
            if (keyboard[Key.W].isPressed || keyboard[Key.UpArrow].isPressed)
                s.AimPitch = Mathf.Clamp(
                    s.AimPitch - s.AimPitchSpeed * Time.deltaTime,
                    s.AimPitchMin,
                    s.AimPitchMax
                );
            if (keyboard[Key.S].isPressed || keyboard[Key.DownArrow].isPressed)
                s.AimPitch = Mathf.Clamp(
                    s.AimPitch + s.AimPitchSpeed * Time.deltaTime,
                    s.AimPitchMin,
                    s.AimPitchMax
                );

            s.AimYaw = Mathf.Clamp(s.AimYaw, -s.AimYawMax, s.AimYawMax);
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

        public static IEnumerator AC130RoutineOld(PlayerInventory inventory)
        {
            if (_isActive)
                yield break;

            _isActive = true;

            int equippedIndex = inventory.EquippedItemIndex;
            SetCurrentItemUse(inventory, ItemUseType.Regular);
            if (equippedIndex >= 0)
                ItemHelper.DecrementAndRemove(inventory, equippedIndex);
            SetCurrentItemUse(inventory, ItemUseType.None);

            yield return new WaitForSeconds(0.01f);

            InputManager.Controls.Gameplay.Disable();

            OrbitCameraModule orbitModule = null;
            CameraModuleController.TryGetOrbitModule(out orbitModule);

            float savedPitch = orbitModule?.Pitch ?? 0f;
            float savedYaw = orbitModule?.Yaw ?? 0f;
            bool savedDisablePhysics = false;

            float baseOrbitSpeed = Configuration.AC130OrbitSpeed.Value;
            float boostedOrbitSpeed = baseOrbitSpeed * Configuration.AC130BoostMultiplier.Value;
            float altitudeOffset = 0f;
            float altitudeOffsetMax = Configuration.AC130AltitudeOffsetMax.Value;
            float altitudeAdjustSpeed = Configuration.AC130AltitudeAdjustSpeed.Value;

            // Pivot is the map centre — the gunship orbits around it.
            Vector3 mapCentre = GetMapCentre(inventory);
            float orbitRadius = Configuration.AC130OrbitRadius.Value;
            float altitude = Configuration.AC130Altitude.Value;
            float orbitSpeed = Configuration.AC130OrbitSpeed.Value; // degrees per second
            float duration = Configuration.AC130Duration.Value;

            // Camera pivot follows the gunship.
            var pivotGo = new GameObject("AC130Pivot");
            pivotGo.transform.position = mapCentre;

            if (orbitModule != null)
            {
                savedDisablePhysics = orbitModule.disablePhysics;
                orbitModule.SetSubject(pivotGo.transform);
                orbitModule.SetPitch(Configuration.AC130CameraPitch.Value);
                orbitModule.SetDistanceAddition(Configuration.AC130CameraDistance.Value);
                orbitModule.disablePhysics = true;
                orbitModule.ForceUpdateModule();
            }

            // Spawn gunship visual.
            float startAngle = 0f;
            Vector3 startPos = AC130Helpers.OrbitPosition(
                mapCentre,
                startAngle,
                orbitRadius,
                altitude
            );
            Vector3 startForward = AC130Helpers.OrbitTangent(startAngle);

            GameObject gunshipVisual = null;
            AC130FlyBehaviour flyComp = null;

            if (AssetLoader.AC130Prefab != null)
            {
                gunshipVisual = Object.Instantiate(
                    AssetLoader.AC130Prefab,
                    startPos,
                    Quaternion.LookRotation(startForward, Vector3.up)
                );

                flyComp = gunshipVisual.AddComponent<AC130FlyBehaviour>();
                flyComp.mapCentre = mapCentre;
                flyComp.orbitRadius = orbitRadius;
                flyComp.altitude = altitude;
                flyComp.orbitSpeed = orbitSpeed;
                flyComp.currentAngle = startAngle;
            }
            else
            {
                IssaPluginPlugin.Log.LogInfo("[AC130] No prefab found, running without visual.");
            }

            // Aim state.
            float aimYaw = 0f; // relative to the gunship's current facing, in degrees
            float aimPitch = Configuration.AC130AimPitchDefault.Value;

            float aimYawSpeed = Configuration.AC130AimYawSpeed.Value;
            float aimPitchSpeed = Configuration.AC130AimPitchSpeed.Value;
            float aimPitchMin = Configuration.AC130AimPitchMin.Value;
            float aimPitchMax = Configuration.AC130AimPitchMax.Value;

            float elapsed = 0f;
            float cooldown = 0f;
            float fireCooldown = Configuration.AC130FireCooldown.Value;

            IssaPluginPlugin.Log.LogInfo(
                $"[AC130] Active for {duration:F0}s, orbit radius {orbitRadius:F0}m, altitude {altitude:F0}m."
            );

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                cooldown -= Time.deltaTime;

                var keyboard = Keyboard.current;
                var mouse = Mouse.current;

                // Early exit.
                if (keyboard != null && keyboard[Key.Space].wasPressedThisFrame)
                {
                    IssaPluginPlugin.Log.LogInfo("[AC130] Player exited early.");
                    break;
                }

                // Speed boost.
                bool boosting = keyboard != null && keyboard[Key.LeftShift].isPressed;
                if (flyComp != null)
                    flyComp.orbitSpeed = boosting ? boostedOrbitSpeed : baseOrbitSpeed;

                // Altitude adjustment.
                if (keyboard != null)
                {
                    if (keyboard[Key.Q].isPressed)
                        altitudeOffset -= altitudeAdjustSpeed * Time.deltaTime;
                    if (keyboard[Key.E].isPressed)
                        altitudeOffset += altitudeAdjustSpeed * Time.deltaTime;

                    altitudeOffset = Mathf.Clamp(
                        altitudeOffset,
                        -altitudeOffsetMax,
                        altitudeOffsetMax
                    );

                    if (flyComp != null)
                        flyComp.altitude = altitude + altitudeOffset;
                }

                // Update gunship position from fly component.
                Vector3 gunshipPos =
                    gunshipVisual != null
                        ? gunshipVisual.transform.position
                        : AC130Helpers.OrbitPosition(
                            mapCentre,
                            elapsed * baseOrbitSpeed,
                            orbitRadius,
                            altitude + altitudeOffset
                        );
                Vector3 gunshipFacing =
                    gunshipVisual != null
                        ? gunshipVisual.transform.forward
                        : AC130Helpers.OrbitTangent(elapsed * baseOrbitSpeed);

                GunshipPosition = gunshipPos;

                pivotGo.transform.position = gunshipPos;
                if (orbitModule != null)
                    orbitModule.ForceUpdateModule();

                // Aim controls — Q/E are now consumed by altitude so removed from here.
                if (keyboard != null)
                {
                    if (keyboard[Key.A].isPressed || keyboard[Key.LeftArrow].isPressed)
                        aimYaw -= aimYawSpeed * Time.deltaTime;
                    if (keyboard[Key.D].isPressed || keyboard[Key.RightArrow].isPressed)
                        aimYaw += aimYawSpeed * Time.deltaTime;
                    if (keyboard[Key.W].isPressed || keyboard[Key.UpArrow].isPressed)
                        aimPitch = Mathf.Clamp(
                            aimPitch - aimPitchSpeed * Time.deltaTime,
                            aimPitchMin,
                            aimPitchMax
                        );
                    if (keyboard[Key.S].isPressed || keyboard[Key.DownArrow].isPressed)
                        aimPitch = Mathf.Clamp(
                            aimPitch + aimPitchSpeed * Time.deltaTime,
                            aimPitchMin,
                            aimPitchMax
                        );
                }

                aimYaw = Mathf.Clamp(
                    aimYaw,
                    -Configuration.AC130AimYawMax.Value,
                    Configuration.AC130AimYawMax.Value
                );

                Quaternion aimRotation =
                    Quaternion.LookRotation(gunshipFacing, Vector3.up)
                    * Quaternion.Euler(aimPitch, aimYaw, 0f);
                Vector3 fireDirection = aimRotation * Vector3.forward;

                Vector3 crosshairWorld = ProjectAimToGround(gunshipPos, fireDirection);
                AC130Overlay.UpdateAimInfo(crosshairWorld, elapsed, duration);

                bool firePressed =
                    (keyboard != null && keyboard[Key.F].wasPressedThisFrame)
                    || (mouse != null && mouse.leftButton.wasPressedThisFrame);

                if (firePressed && cooldown <= 0f)
                {
                    float angularJitter = Configuration.AC130RocketAngularJitter.Value;
                    Quaternion jitter = Quaternion.Euler(
                        Random.Range(-angularJitter, angularJitter),
                        Random.Range(-angularJitter, angularJitter),
                        0f
                    );

                    SpawnRocketInDirection(inventory, gunshipPos, jitter * aimRotation);
                    IssaPluginPlugin.Log.LogInfo($"[AC130] Rocket fired toward {crosshairWorld}.");
                    cooldown = fireCooldown;
                }

                yield return null;
            }

            // Cleanup.
            IssaPluginPlugin.Log.LogInfo("[AC130] Time expired.");

            if (gunshipVisual != null)
                Object.Destroy(gunshipVisual);

            Object.Destroy(pivotGo);
            _isActive = false;

            RestoreCamera(orbitModule, savedPitch, savedYaw, savedDisablePhysics);
            InputManager.Controls.Gameplay.Enable();
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

        private static void SpawnRocketInDirection(
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
