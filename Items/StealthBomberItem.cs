using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Mirror;
using UnityEngine;
using UnityEngine.InputSystem;

namespace IssaPlugin.Items
{
    public static class StealthBomberItem
    {
        public static readonly ItemType BomberItemType = (ItemType)101;

        private static bool _isBombing;
        private static bool _isTargeting;
        private static int _bomberUseIndex;

        public static bool IsTargeting => _isTargeting;

        public static void GiveBomberToLocalPlayer()
        {
            ItemHelper.GiveItemToLocalPlayer(
                BomberItemType,
                Configuration.BomberUses.Value,
                "Bomber"
            );
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
                IssaPluginPlugin.Log.LogError("[Bomber] Could not find SetCurrentItemUse method.");
        }

        public static IEnumerator BomberRunRoutine(PlayerInventory inventory)
        {
            if (_isBombing || _isTargeting)
                yield break;

            int equippedIndex = inventory.EquippedItemIndex;

            // ============================================================
            //  Phase 1 — Bird's-eye targeting
            // ============================================================
            _isTargeting = true;
            InputManager.Controls.Gameplay.Disable();

            OrbitCameraModule orbitModule = null;
            CameraModuleController.TryGetOrbitModule(out orbitModule);

            float savedPitch = 0f;
            float savedYaw = 0f;
            if (orbitModule != null)
            {
                savedPitch = orbitModule.Pitch;
                savedYaw = orbitModule.Yaw;
            }

            var playerPos = inventory.PlayerInfo.transform.position;

            var pivotGo = new GameObject("BomberTargetPivot");
            pivotGo.transform.position = playerPos;

            float stripLength = Configuration.BomberStripLength.Value;
            float stripWidth = Configuration.BomberSpread.Value * 2f;
            float stripAngle = 0f;

            var stripGo = CreateStripVisual(playerPos, stripWidth, stripLength);

            bool savedDisablePhysics = false;
            if (orbitModule != null)
            {
                savedDisablePhysics = orbitModule.disablePhysics;

                orbitModule.SetSubject(pivotGo.transform);
                orbitModule.SetPitch(88f);
                orbitModule.SetDistanceAddition(
                    Configuration.BomberTargetingAltitude.Value * 1.10f
                );
                orbitModule.disablePhysics = true;
                orbitModule.ForceUpdateModule();
            }

            yield return null;

            float moveSpeed = Configuration.BomberTargetMoveSpeed.Value;
            float rotateSpeed = Configuration.BomberTargetRotateSpeed.Value;
            bool confirmed = false;
            bool cancelled = false;

            while (!confirmed && !cancelled)
            {
                var keyboard = Keyboard.current;
                var mouse = Mouse.current;
                if (keyboard == null)
                {
                    yield return null;
                    continue;
                }

                float inputX = 0f;
                float inputZ = 0f;
                if (keyboard[Key.W].isPressed || keyboard[Key.UpArrow].isPressed)
                    inputZ += 1f;
                if (keyboard[Key.S].isPressed || keyboard[Key.DownArrow].isPressed)
                    inputZ -= 1f;
                if (keyboard[Key.A].isPressed || keyboard[Key.LeftArrow].isPressed)
                    inputX -= 1f;
                if (keyboard[Key.D].isPressed || keyboard[Key.RightArrow].isPressed)
                    inputX += 1f;

                if (inputX != 0f || inputZ != 0f)
                {
                    Vector3 camForward = Vector3.forward;
                    Vector3 camRight = Vector3.right;
                    if (orbitModule != null)
                    {
                        float yawRad = orbitModule.Yaw * Mathf.Deg2Rad;
                        camForward = new Vector3(Mathf.Sin(yawRad), 0f, Mathf.Cos(yawRad));
                        camRight = new Vector3(Mathf.Cos(yawRad), 0f, -Mathf.Sin(yawRad));
                    }

                    Vector3 worldMove =
                        (camRight * inputX + camForward * inputZ) * moveSpeed * Time.deltaTime;
                    stripGo.transform.position += worldMove;
                    pivotGo.transform.position = new Vector3(
                        stripGo.transform.position.x,
                        pivotGo.transform.position.y,
                        stripGo.transform.position.z
                    );
                }

                if (keyboard[Key.Q].isPressed)
                    stripAngle -= rotateSpeed * Time.deltaTime;
                if (keyboard[Key.E].isPressed)
                    stripAngle += rotateSpeed * Time.deltaTime;

                stripGo.transform.rotation = Quaternion.Euler(0f, stripAngle, 0f);

                if (
                    keyboard[Key.Enter].wasPressedThisFrame
                    || (mouse != null && mouse.leftButton.wasPressedThisFrame)
                )
                    confirmed = true;

                if (
                    keyboard[Key.Escape].wasPressedThisFrame
                    || (mouse != null && mouse.rightButton.wasPressedThisFrame)
                )
                    cancelled = true;

                yield return null;
            }

            Vector3 stripCenter = stripGo.transform.position;
            Vector3 stripForward = stripGo.transform.forward;

            Object.Destroy(stripGo);
            Object.Destroy(pivotGo);
            _isTargeting = false;

            RestoreCamera(orbitModule, savedPitch, savedYaw, savedDisablePhysics);
            InputManager.Controls.Gameplay.Enable();

            if (cancelled)
            {
                IssaPluginPlugin.Log.LogInfo("[Bomber] Targeting cancelled.");
                yield break;
            }

            // ============================================================
            //  Phase 2 — Execute bombing run along the confirmed line
            // ============================================================
            _isBombing = true;

            SetCurrentItemUse(inventory, ItemUseType.Regular);
            if (equippedIndex >= 0)
                ItemHelper.DecrementAndRemove(inventory, equippedIndex);
            SetCurrentItemUse(inventory, ItemUseType.None);

            yield return new WaitForSeconds(0.01f);
            yield return new WaitForSeconds(Configuration.BomberWaitTime.Value);

            float altitude = Configuration.BomberAltitude.Value;
            int rocketCount = Configuration.BomberRocketCount.Value;
            float rocketInterval = Configuration.BomberRocketInterval.Value;
            float speed = Configuration.BomberSpeed.Value;
            float spread = Configuration.BomberSpread.Value;
            float approachDist = Configuration.BomberApproachDistance.Value;

            float halfLength = stripLength / 2f;
            Vector3 stripStart = stripCenter - stripForward * halfLength;
            Vector3 stripEnd = stripCenter + stripForward * halfLength;
            Vector3 direction = (stripEnd - stripStart).normalized;
            float totalDistance = Vector3.Distance(stripStart, stripEnd);
            Vector3 perpendicular = Vector3.Cross(direction, Vector3.up).normalized;

            // Extend the flight path beyond the strip in both directions.
            Vector3 spawnPos = stripStart - direction * approachDist;
            Vector3 exitPos = stripEnd + direction * approachDist;
            spawnPos.y = stripCenter.y + altitude;
            exitPos.y = stripCenter.y + altitude;

            // Evenly space drop waypoints across the bombing strip only.
            float rocketSpacing = totalDistance / (rocketCount + 1);
            var dropWaypoints = new List<Vector3>(rocketCount);
            for (int i = 1; i <= rocketCount; i++)
            {
                Vector3 wp = stripStart + direction * (rocketSpacing * i);
                wp.y = stripCenter.y + altitude;
                dropWaypoints.Add(wp);
            }

            IssaPluginPlugin.Log.LogInfo(
                $"[Bomber] Run started: {rocketCount} rockets over {totalDistance:F0}m "
                    + $"at altitude {altitude:F0}m, approach {approachDist:F0}m"
            );

            GameObject bomberVisual = null;
            if (AssetLoader.BomberPrefab != null)
            {
                IssaPluginPlugin.Log.LogInfo("[Bomber] Spawning bomber visual.");
                bomberVisual = Object.Instantiate(
                    AssetLoader.BomberPrefab,
                    spawnPos, // <-- far approach point
                    Quaternion.LookRotation(direction, Vector3.up)
                );

                var flyComp = bomberVisual.AddComponent<BomberFlyBehaviour>();
                flyComp.destination = exitPos; // <-- far exit point
                flyComp.speed = speed;
            }
            else
            {
                IssaPluginPlugin.Log.LogInfo(
                    "[Bomber] Cannot spawn bomber visual; cannot find prefab."
                );
            }

            int rocketsDropped = 0;

            while (rocketsDropped < rocketCount)
            {
                Vector3 bomberPos =
                    bomberVisual != null
                        ? bomberVisual.transform.position
                        : dropWaypoints[rocketsDropped];

                float distToWaypoint = Vector3.Distance(bomberPos, dropWaypoints[rocketsDropped]);
                float distToEnd = Vector3.Distance(bomberPos, exitPos);
                bool pastWaypoint = distToWaypoint > distToEnd;

                if (!pastWaypoint)
                {
                    yield return null;
                    continue;
                }

                Vector3 offset = perpendicular * Random.Range(-spread, spread);
                SpawnRocket(inventory, bomberPos + offset);
                rocketsDropped++;

                yield return new WaitForSeconds(rocketInterval);
            }

            // Let the bomber finish flying to the exit point before cleaning up.
            while (bomberVisual != null)
                yield return null;

            IssaPluginPlugin.Log.LogInfo(
                $"[Bomber] Run complete. {rocketsDropped} rockets dropped."
            );
            _isBombing = false;
        }

        private static GameObject CreateStripVisual(Vector3 center, float width, float length)
        {
            var root = new GameObject("BomberTargetStrip");
            root.transform.position = new Vector3(center.x, center.y + 0.5f, center.z);

            CreateStripPart(
                root.transform,
                "StripBody",
                Vector3.zero,
                new Vector3(width, 0.2f, length),
                new Color(1f, 0.3f, 0f, 0.3f)
            );

            AddDirectionChevrons(root.transform, width, length);

            return root;
        }

        private static void AddDirectionChevrons(Transform parent, float width, float length)
        {
            float armLength = Mathf.Max(width * 0.6f, 4f);
            float halfSpread = width * 0.35f;
            float armThickness = Mathf.Max(width * 0.08f, 0.6f);
            float actualLength = Mathf.Sqrt(halfSpread * halfSpread + armLength * armLength);

            var color = new Color(1f, 0.15f, 0.15f, 0.55f);
            float[] zPositions = { length * 0.15f, -length * 0.15f };

            foreach (float z in zPositions)
            {
                Vector3 dirL = new Vector3(-halfSpread, 0f, armLength).normalized;
                CreateStripPart(
                    parent,
                    "ChevronL",
                    new Vector3(halfSpread * 0.5f, 0.15f, z),
                    new Vector3(armThickness, 0.25f, actualLength),
                    color,
                    Quaternion.LookRotation(dirL, Vector3.up)
                );

                Vector3 dirR = new Vector3(halfSpread, 0f, armLength).normalized;
                CreateStripPart(
                    parent,
                    "ChevronR",
                    new Vector3(-halfSpread * 0.5f, 0.15f, z),
                    new Vector3(armThickness, 0.25f, actualLength),
                    color,
                    Quaternion.LookRotation(dirR, Vector3.up)
                );
            }
        }

        private static GameObject CreateStripPart(
            Transform parent,
            string name,
            Vector3 localPos,
            Vector3 localScale,
            Color color,
            Quaternion? localRotation = null
        )
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = localScale;
            if (localRotation.HasValue)
                go.transform.localRotation = localRotation.Value;

            var col = go.GetComponent<Collider>();
            if (col != null)
                Object.Destroy(col);

            var renderer = go.GetComponent<Renderer>();
            if (renderer != null)
            {
                var shader =
                    Shader.Find("Sprites/Default")
                    ?? Shader.Find("UI/Default")
                    ?? Shader.Find("Unlit/Color");

                var mat = new Material(shader);
                mat.color = color;
                mat.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.Always);
                mat.SetInt("_ZWrite", 0);
                mat.renderQueue = 3100;
                renderer.material = mat;
            }

            return go;
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

        private static void SpawnRocket(PlayerInventory inventory, Vector3 position)
        {
            if (!NetworkServer.active)
                return;

            Quaternion rotation = Quaternion.LookRotation(Vector3.down, Vector3.forward);

            _bomberUseIndex++;
            var itemUseId = new ItemUseId(
                inventory.PlayerInfo.PlayerId.Guid,
                _bomberUseIndex,
                ItemType.RocketLauncher
            );

            var rocket = Object.Instantiate(
                GameManager.ItemSettings.RocketPrefab,
                position,
                rotation
            );

            if (rocket == null)
            {
                IssaPluginPlugin.Log.LogError("[Bomber] Rocket did not instantiate.");
                return;
            }

            rocket.ServerInitialize(inventory.PlayerInfo, null, itemUseId);
            NetworkServer.Spawn(rocket.gameObject, (NetworkConnectionToClient)null);
        }

        /// <summary>
        /// Attached to the bomber prefab instance so it flies smoothly
        /// from spawn to destination independent of the rocket-drop coroutine.
        /// </summary>
        private class BomberFlyBehaviour : MonoBehaviour
        {
            public Vector3 destination;
            public float speed;

            private void Update()
            {
                transform.position = Vector3.MoveTowards(
                    transform.position,
                    destination,
                    speed * Time.deltaTime
                );

                if (Vector3.Distance(transform.position, destination) < 0.5f)
                    Destroy(gameObject);
            }
        }
    }
}
