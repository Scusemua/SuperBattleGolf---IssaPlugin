using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Mirror;
using UnityEngine;
using UnityEngine.InputSystem;

namespace IssaPlugin.Items
{
    public static class PredatorMissileItem
    {
        public static readonly ItemType MissileItemType = (ItemType)102;

        private static bool _isSteering;
        private static int _missileUseIndex;

        internal static Rocket ActiveMissileRocket;
        internal static readonly List<GameObject> DebugDummies = new List<GameObject>();

        public static bool IsSteering => _isSteering;

        public static void GiveMissileToLocalPlayer()
        {
            ItemHelper.GiveItemToLocalPlayer(
                MissileItemType,
                Configuration.MissileUses.Value,
                "Missile"
            );
        }

        public static void ToggleDebugDummies()
        {
            if (DebugDummies.Count > 0)
            {
                foreach (var dummy in DebugDummies)
                {
                    if (dummy != null)
                        Object.Destroy(dummy);
                }
                DebugDummies.Clear();
                IssaPluginPlugin.Log.LogInfo("[Missile] Debug dummies removed.");
                return;
            }

            var playerPos = GameManager.LocalPlayerInfo?.transform.position ?? Vector3.zero;

            Vector3[] offsets =
            {
                new Vector3(10f, 0f, 10f),
                new Vector3(-12f, 0f, 5f),
                new Vector3(5f, 0f, -15f),
                new Vector3(-8f, 0f, -8f),
                new Vector3(18f, 0f, 0f),
            };

            foreach (var offset in offsets)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                go.name = "MissileDummy";
                go.transform.position = playerPos + offset;
                go.transform.localScale = new Vector3(0.6f, 1f, 0.6f);

                var col = go.GetComponent<Collider>();
                if (col != null)
                    Object.Destroy(col);

                var renderer = go.GetComponent<Renderer>();
                if (renderer != null)
                    renderer.material.color = new Color(1f, 0.3f, 0.3f, 0.7f);

                DebugDummies.Add(go);
            }

            IssaPluginPlugin.Log.LogInfo(
                $"[Missile] Spawned {offsets.Length} debug dummies near {playerPos}."
            );
        }

        public static IEnumerator MissileRoutine(PlayerInventory inventory)
        {
            if (_isSteering)
                yield break;
            if (!NetworkServer.active)
                yield break;

            _isSteering = true;

            int slotIndex = inventory.EquippedItemIndex;
            if (slotIndex >= 0)
                ItemHelper.DecrementAndRemove(inventory, slotIndex);

            var playerInfo = inventory.PlayerInfo;
            var playerTransform = playerInfo.transform;
            float altitude = Configuration.MissileAltitude.Value;
            float fallSpeed = Configuration.MissileFallSpeed.Value;
            float steerSpeed = Configuration.MissileSteerSpeed.Value;
            float timeout = Configuration.MissileTimeout.Value;

            Vector3 spawnPos = playerTransform.position + Vector3.up * altitude;
            Quaternion spawnRot = Quaternion.LookRotation(Vector3.down, Vector3.forward);

            _missileUseIndex++;
            var itemUseId = new ItemUseId(
                playerInfo.PlayerId.Guid,
                _missileUseIndex,
                ItemType.RocketLauncher
            );

            var rocket = Object.Instantiate(
                GameManager.ItemSettings.RocketPrefab,
                spawnPos,
                spawnRot
            );

            if (rocket == null)
            {
                IssaPluginPlugin.Log.LogError("[Missile] Rocket did not instantiate.");
                _isSteering = false;
                yield break;
            }

            rocket.ServerInitialize(playerInfo, null, itemUseId);
            ActiveMissileRocket = rocket;
            NetworkServer.Spawn(rocket.gameObject, (NetworkConnectionToClient)null);

            IssaPluginPlugin.Log.LogInfo(
                $"[Missile] Launched at {spawnPos}, falling at {fallSpeed} m/s."
            );

            yield return null;

            InputManager.Controls.Gameplay.Disable();

            OrbitCameraModule orbitModule = null;
            CameraModuleController.TryGetOrbitModule(out orbitModule);

            float savedPitch = 0f;
            float savedYaw = 0f;

            if (orbitModule != null)
            {
                savedPitch = orbitModule.Pitch;
                savedYaw = orbitModule.Yaw;

                orbitModule.SetSubject(rocket.transform);
                orbitModule.SetPitch(80f);
                orbitModule.ForceUpdateModule();
            }

            var rocketRb = rocket.GetComponent<Rigidbody>();
            if (rocketRb == null)
            {
                var entity = rocket.GetComponent<Entity>();
                if (entity != null && entity.HasRigidbody)
                    rocketRb = entity.Rigidbody;
            }

            float elapsed = 0f;
            bool rocketAlive = true;

            while (rocketAlive && elapsed < timeout)
            {
                if (rocket == null || rocket.gameObject == null)
                {
                    rocketAlive = false;
                    break;
                }

                var keyboard = Keyboard.current;
                float inputX = 0f;
                float inputZ = 0f;

                if (keyboard != null)
                {
                    if (keyboard[Key.W].isPressed || keyboard[Key.UpArrow].isPressed)
                        inputZ += 1f;
                    if (keyboard[Key.S].isPressed || keyboard[Key.DownArrow].isPressed)
                        inputZ -= 1f;
                    if (keyboard[Key.A].isPressed || keyboard[Key.LeftArrow].isPressed)
                        inputX -= 1f;
                    if (keyboard[Key.D].isPressed || keyboard[Key.RightArrow].isPressed)
                        inputX += 1f;
                }

                if (rocketRb != null)
                {
                    Vector3 camForward = Vector3.forward;
                    Vector3 camRight = Vector3.right;

                    if (orbitModule != null)
                    {
                        float yawRad = orbitModule.Yaw * Mathf.Deg2Rad;
                        camForward = new Vector3(Mathf.Sin(yawRad), 0f, Mathf.Cos(yawRad));
                        camRight = new Vector3(Mathf.Cos(yawRad), 0f, -Mathf.Sin(yawRad));
                    }

                    Vector3 steer = (camRight * inputX + camForward * inputZ) * steerSpeed;

                    rocketRb.linearVelocity = new Vector3(steer.x, -fallSpeed, steer.z);
                }

                elapsed += Time.deltaTime;
                yield return null;
            }

            InputManager.Controls.Gameplay.Enable();

            if (orbitModule != null)
            {
                var playerMovement = GameManager.LocalPlayerMovement;
                if (playerMovement != null)
                    orbitModule.SetSubject(playerMovement.transform);

                orbitModule.SetPitch(savedPitch);
                orbitModule.SetYaw(savedYaw);
                orbitModule.ForceUpdateModule();
            }

            if (rocketAlive && rocket != null && rocket.gameObject != null)
            {
                var explodeMethod = typeof(Rocket).GetMethod(
                    "ServerExplode",
                    BindingFlags.NonPublic | BindingFlags.Instance
                );
                explodeMethod?.Invoke(rocket, new object[] { rocket.transform.position });
            }

            ActiveMissileRocket = null;
            _isSteering = false;
            IssaPluginPlugin.Log.LogInfo("[Missile] Steering ended.");
        }
    }
}
