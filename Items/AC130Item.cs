using System.Collections;
using System.Reflection;
using Mirror;
using UnityEngine;
using UnityEngine.InputSystem;

namespace IssaPlugin.Items
{
    public static class AC130Item
    {
        public static readonly ItemType AC130ItemType = (ItemType)102;

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
            Vector3 startPos = OrbitPosition(mapCentre, startAngle, orbitRadius, altitude);
            Vector3 startForward = OrbitTangent(startAngle);

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

                // Update gunship position from fly component (or fallback).
                Vector3 gunshipPos =
                    gunshipVisual != null
                        ? gunshipVisual.transform.position
                        : OrbitPosition(mapCentre, elapsed * orbitSpeed, orbitRadius, altitude);
                Vector3 gunshipFacing =
                    gunshipVisual != null
                        ? gunshipVisual.transform.forward
                        : OrbitTangent(elapsed * orbitSpeed);

                GunshipPosition = gunshipPos;

                // Keep camera pivot locked to gunship position.
                pivotGo.transform.position = gunshipPos;
                if (orbitModule != null)
                    orbitModule.ForceUpdateModule();

                // Aim controls — yaw sweeps left/right relative to gunship facing,
                // pitch tilts the barrel up/down.
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

                // Clamp yaw so the gunship can only fire downward toward the map,
                // not back over itself.
                aimYaw = Mathf.Clamp(
                    aimYaw,
                    -Configuration.AC130AimYawMax.Value,
                    Configuration.AC130AimYawMax.Value
                );

                // Resolve world-space fire direction from aim angles.
                Quaternion aimRotation =
                    Quaternion.LookRotation(gunshipFacing, Vector3.up)
                    * Quaternion.Euler(aimPitch, aimYaw, 0f);
                Vector3 fireDirection = aimRotation * Vector3.forward;

                // Crosshair world position — cast from gunship along aim direction
                // and find where it hits the ground plane.
                Vector3 crosshairWorld = ProjectAimToGround(gunshipPos, fireDirection);
                AC130Overlay.UpdateAimInfo(crosshairWorld, elapsed, duration);

                // Fire.
                bool firePressed =
                    (keyboard != null && keyboard[Key.Space].wasPressedThisFrame)
                    || (mouse != null && mouse.leftButton.wasPressedThisFrame);

                if (firePressed && cooldown <= 0f)
                {
                    float angularJitter = Configuration.AC130RocketAngularJitter.Value;
                    Quaternion jitter = Quaternion.Euler(
                        Random.Range(-angularJitter, angularJitter),
                        Random.Range(-angularJitter, angularJitter),
                        0f
                    );

                    SpawnRocket(inventory, gunshipPos, jitter * aimRotation);

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

        // ----------------------------------------------------------------
        //  Helpers
        // ----------------------------------------------------------------
        private static Vector3 OrbitPosition(
            Vector3 centre,
            float angleDeg,
            float radius,
            float altitude
        )
        {
            float rad = angleDeg * Mathf.Deg2Rad;
            return new Vector3(
                centre.x + Mathf.Cos(rad) * radius,
                centre.y + altitude,
                centre.z + Mathf.Sin(rad) * radius
            );
        }

        private static Vector3 OrbitTangent(float angleDeg)
        {
            // Derivative of OrbitPosition with respect to angle, normalised.
            float rad = angleDeg * Mathf.Deg2Rad;
            return new Vector3(-Mathf.Sin(rad), 0f, Mathf.Cos(rad)).normalized;
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

        private static Vector3 GetMapCentre(PlayerInventory inventory)
        {
            // Use the player's current position projected to ground as the
            // orbit centre so the gunship always circles the action.
            var pos = inventory.PlayerInfo.transform.position;
            return new Vector3(pos.x, 0f, pos.z);
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

        // ----------------------------------------------------------------
        //  Gunship movement
        // ----------------------------------------------------------------
        private class AC130FlyBehaviour : MonoBehaviour
        {
            public Vector3 mapCentre;
            public float orbitRadius;
            public float altitude;
            public float orbitSpeed; // degrees per second
            public float currentAngle;

            private void Update()
            {
                currentAngle += orbitSpeed * Time.deltaTime;

                float rad = currentAngle * Mathf.Deg2Rad;
                transform.position = new Vector3(
                    mapCentre.x + Mathf.Cos(rad) * orbitRadius,
                    mapCentre.y + altitude,
                    mapCentre.z + Mathf.Sin(rad) * orbitRadius
                );

                // Always face the tangent direction of the circle.
                Vector3 tangent = new Vector3(-Mathf.Sin(rad), 0f, Mathf.Cos(rad)).normalized;
                if (tangent != Vector3.zero)
                    transform.rotation = Quaternion.LookRotation(tangent, Vector3.up);
            }
        }
    }
}
