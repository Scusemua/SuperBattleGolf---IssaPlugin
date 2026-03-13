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

        // _isTargeting is client-side and per-local-player, so static is fine —
        // only one player's input is processed on any given client.
        // _isBombing has been moved to BomberNetworkBridge as an instance field.
        private static bool _isTargeting;
        private static int _bomberUseIndex;

        public static bool IsTargeting => _isTargeting;

        /// <summary>
        /// The local visual bomber GameObject spawned by LocalSpawnBomberVisual.
        /// Set on all clients; used by BomberNetworkBridge.RpcBomberShotDown to
        /// locate the visual and switch it to crash behaviour.
        /// </summary>
        public static GameObject ActiveBomberVisual { get; private set; }

        private class TargetingResult
        {
            public BombingStripInfo? Strip;
        }

        public struct BombingStripInfo
        {
            public Vector3 Center;
            public Vector3 Forward;
            public float Length;
        }

        public static void GiveBomberToLocalPlayer()
        {
            ItemHelper.GiveItemToLocalPlayer(
                BomberItemType,
                (int)Configuration.BomberUses.Value,
                "Bomber"
            );
        }

        private static void SetCurrentItemUse(PlayerInventory inventory, ItemUseType type)
        {
            ItemHelper.SetCurrentItemUse(inventory, type);
        }

        public static IEnumerator BomberRunRoutine(PlayerInventory inventory)
        {
            if (_isTargeting)
                yield break;

            int equippedIndex = inventory.EquippedItemIndex;

            var result = new TargetingResult();
            var targeting = RunTargetingPhase(inventory, result);
            yield return targeting;

            if (result.Strip == null)
            {
                IssaPluginPlugin.Log.LogInfo("[Bomber] Targeting cancelled.");
                yield break;
            }

            var bridge = inventory.GetComponent<BomberNetworkBridge>();
            if (bridge == null)
            {
                IssaPluginPlugin.Log.LogError("[Bomber] No BomberNetworkBridge on player.");
                yield break;
            }

            bridge.CmdRequestBombingRun(
                result.Strip.Value.Center,
                result.Strip.Value.Forward,
                result.Strip.Value.Length,
                equippedIndex
            );
        }

        private static void HandleTargetingMovement(
            Keyboard keyboard,
            OrbitCameraModule orbitModule,
            float moveSpeed,
            GameObject stripGo,
            GameObject pivotGo
        )
        {
            float inputX = 0f,
                inputZ = 0f;

            if (keyboard[Key.W].isPressed || keyboard[Key.UpArrow].isPressed)
                inputZ += 1f;
            if (keyboard[Key.S].isPressed || keyboard[Key.DownArrow].isPressed)
                inputZ -= 1f;
            if (keyboard[Key.A].isPressed || keyboard[Key.LeftArrow].isPressed)
                inputX -= 1f;
            if (keyboard[Key.D].isPressed || keyboard[Key.RightArrow].isPressed)
                inputX += 1f;

            if (inputX == 0f && inputZ == 0f)
                return;

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

        private static void HandleTargetingRotation(
            Keyboard keyboard,
            float rotateSpeed,
            ref float stripAngle,
            GameObject stripGo
        )
        {
            if (keyboard[Key.Q].isPressed)
                stripAngle -= rotateSpeed * Time.deltaTime;
            if (keyboard[Key.E].isPressed)
                stripAngle += rotateSpeed * Time.deltaTime;

            stripGo.transform.rotation = Quaternion.Euler(0f, stripAngle, 0f);
        }

        private static IEnumerator RunTargetingPhase(
            PlayerInventory inventory,
            TargetingResult result
        )
        {
            _isTargeting = true;
            InputManager.Controls.Gameplay.Disable();

            OrbitCameraModule orbitModule = null;
            CameraModuleController.TryGetOrbitModule(out orbitModule);

            float savedPitch = orbitModule?.Pitch ?? 0f;
            float savedYaw = orbitModule?.Yaw ?? 0f;
            bool savedDisablePhysics = false;

            var playerPos = inventory.PlayerInfo.transform.position;
            var pivotGo = new GameObject("BomberTargetPivot");
            pivotGo.transform.position = playerPos;

            float stripLength = Configuration.BomberStripLength.Value;
            float stripWidth = Configuration.BomberSpread.Value * 2f;
            float stripAngle = 0f;
            var stripGo = CreateStripVisual(playerPos, stripWidth, stripLength);

            float currentDistanceAddition = 0;
            float zoomSpeed = Configuration.BomberTargetingZoomSpeed.Value;

            if (orbitModule != null)
            {
                savedDisablePhysics = orbitModule.disablePhysics;
                orbitModule.SetSubject(pivotGo.transform);
                orbitModule.SetPitch(88f);
                orbitModule.SetDistanceAddition(currentDistanceAddition);
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

                HandleTargetingMovement(keyboard, orbitModule, moveSpeed, stripGo, pivotGo);
                HandleTargetingRotation(keyboard, rotateSpeed, ref stripAngle, stripGo);

                if (orbitModule != null && mouse != null)
                {
                    float scroll = mouse.scroll.ReadValue().y;
                    if (scroll != 0f)
                    {
                        float zoomStep = Mathf.Sign(scroll) * zoomSpeed;
                        currentDistanceAddition -= zoomStep;
                        currentDistanceAddition = Mathf.Clamp(currentDistanceAddition, 1f, 2000f);
                        orbitModule.SetDistanceAddition(currentDistanceAddition);
                    }
                }

                if (
                    keyboard[Key.Enter].wasPressedThisFrame
                    || (mouse != null && mouse.leftButton.wasPressedThisFrame)
                )
                    confirmed = true;

                if (
                    keyboard[Key.Space].wasPressedThisFrame
                    || (mouse != null && mouse.rightButton.wasPressedThisFrame)
                )
                    cancelled = true;

                yield return null;
            }

            if (confirmed)
                result.Strip = new BombingStripInfo
                {
                    Center = stripGo.transform.position,
                    Forward = stripGo.transform.forward,
                    Length = stripLength,
                };

            Object.Destroy(stripGo);
            Object.Destroy(pivotGo);
            _isTargeting = false;

            RestoreCamera(orbitModule, savedPitch, savedYaw, savedDisablePhysics);
            InputManager.Controls.Gameplay.Enable();
        }

        public static void LocalSpawnBomberVisual(
            Vector3 spawnPos,
            Vector3 exitPos,
            Vector3 direction,
            float speed
        )
        {
            ActiveBomberVisual = SpawnBomberVisual(spawnPos, exitPos, direction, speed);
        }

        /// <summary>
        /// Runs the server-side bombing phase.
        /// <paramref name="onComplete"/> is invoked when the run finishes so
        /// the calling bridge can clear its per-instance _isBombing flag.
        /// </summary>
        public static IEnumerator ServerBombingPhase(
            PlayerInventory inventory,
            int equippedIndex,
            BombingStripInfo strip,
            BomberNetworkBridge bridge,
            System.Action onComplete
        )
        {
            SetCurrentItemUse(inventory, ItemUseType.Regular);
            if (equippedIndex >= 0)
                ItemHelper.DecrementAndRemove(inventory, equippedIndex);
            SetCurrentItemUse(inventory, ItemUseType.None);

            yield return new WaitForSeconds(0.01f);
            yield return new WaitForSeconds(Configuration.BomberWaitTime.Value);

            float altitude = Configuration.BomberAltitude.Value;
            float rocketInterval = Configuration.BomberRocketInterval.Value;
            float speed = Configuration.BomberSpeed.Value;
            float spread = Configuration.BomberSpread.Value;
            float approachDist = Configuration.BomberApproachDistance.Value;
            float waitTime = Configuration.BomberWaitTime.Value;

            float halfLength = strip.Length / 2f;
            Vector3 stripStart = strip.Center - strip.Forward * halfLength;
            Vector3 stripEnd = strip.Center + strip.Forward * halfLength;
            Vector3 direction = (stripEnd - stripStart).normalized;
            Vector3 perpendicular = Vector3.Cross(direction, Vector3.up).normalized;

            Vector3 spawnPos = stripStart - direction * approachDist;
            Vector3 exitPos = stripEnd + direction * approachDist;
            spawnPos.y = strip.Center.y + altitude;
            exitPos.y = strip.Center.y + altitude;

            float exitBufferDist = speed * waitTime;
            float dropEndDist = Vector3.Distance(spawnPos, exitPos) - exitBufferDist;
            float totalDist = Vector3.Distance(spawnPos, exitPos);

            IssaPluginPlugin.Log.LogInfo(
                $"[Bomber] Run started: dropping rockets at interval {rocketInterval:F0} over {strip.Length:F0}m "
                    + $"at altitude {altitude:F0}m, approach {approachDist:F0}m"
            );

            // Spawn a server-side proxy that travels the same path as the visual
            // bomber, giving the game's lock-on system a valid networked target.
            bool proxyShotDown = false;
            BomberProxyBehaviour proxyBehaviour = null;
            GameObject proxyGo = SpawnBomberProxy(
                spawnPos,
                direction,
                speed,
                totalDist,
                () =>
                {
                    proxyShotDown = true;

                    // Compute a push direction from the rocket explosion outward
                    // through the proxy center. Falls back to zero if unavailable.
                    Vector3 rocketImpactDir = Vector3.zero;
                    if (proxyBehaviour != null && proxyBehaviour.LastHitWorldPos != Vector3.zero)
                    {
                        rocketImpactDir = (
                            proxyBehaviour.transform.position - proxyBehaviour.LastHitWorldPos
                        ).normalized;
                    }

                    // Generate tumble torque on the server so all clients receive the
                    // same value via the RPC rather than each rolling independently.
                    Vector3 torqueImpulse =
                        UnityEngine.Random.insideUnitSphere * Configuration.BomberCrashTorque.Value;

                    bridge.RpcBomberShotDown(direction, speed, rocketImpactDir, torqueImpulse);
                }
            );
            proxyBehaviour = proxyGo?.GetComponent<BomberProxyBehaviour>();
            if (proxyGo != null)
                bridge.RpcAddBomberLockOnComponents(proxyGo.GetComponent<NetworkIdentity>());

            bridge.RpcSpawnBomberVisual(spawnPos, exitPos, direction, speed);

            float startTime = Time.time;
            int rocketsDropped = 0;
            bool droppingFinished = false;

            while (true)
            {
                float elapsed = Time.time - startTime;
                float distanceTravelled = elapsed * speed;

                // Abort early if the bomber was shot down.
                if (proxyShotDown || proxyGo == null)
                    break;

                if (distanceTravelled >= totalDist)
                    break;

                Vector3 bomberPos = spawnPos + direction * distanceTravelled;

                if (!droppingFinished)
                {
                    if (distanceTravelled >= dropEndDist)
                    {
                        droppingFinished = true;
                    }
                    else
                    {
                        Vector3 offset = perpendicular * UnityEngine.Random.Range(-spread, spread);
                        float angularJitter = Configuration.BomberRocketAngularJitter.Value;
                        Quaternion jitter = Quaternion.Euler(
                            UnityEngine.Random.Range(-angularJitter, angularJitter),
                            UnityEngine.Random.Range(-angularJitter, angularJitter),
                            0f
                        );
                        SpawnRocket(inventory, bomberPos + offset, jitter);
                        rocketsDropped++;

                        yield return new WaitForSeconds(rocketInterval);
                        continue;
                    }
                }

                yield return null;
            }

            IssaPluginPlugin.Log.LogInfo(
                $"[Bomber] Run complete. {rocketsDropped} rockets dropped."
            );

            // Clean up the proxy if it wasn't already destroyed by a hit.
            if (proxyGo != null)
                Object.Destroy(proxyGo);

            onComplete?.Invoke();
        }

        private static GameObject SpawnBomberProxy(
            Vector3 spawnPos,
            Vector3 direction,
            float speed,
            float totalDist,
            System.Action onShotDown
        )
        {
            var bomberProxyGo = Object.Instantiate(
                AssetLoader.BomberProxyPrefab,
                spawnPos,
                Quaternion.LookRotation(direction, Vector3.up)
            );

            bomberProxyGo.AddComponent<Entity>(); // required by LockOnTarget.Awake on clients

            var proxy = bomberProxyGo.AddComponent<BomberProxyBehaviour>();
            proxy.SpawnPos = spawnPos;
            proxy.Direction = direction;
            proxy.Speed = speed;
            proxy.TotalDist = totalDist;
            proxy.OnHitsExceeded = onShotDown;

            NetworkServer.Spawn(bomberProxyGo);
            return bomberProxyGo;
        }

        private static GameObject SpawnBomberVisual(
            Vector3 spawnPos,
            Vector3 exitPos,
            Vector3 direction,
            float speed
        )
        {
            if (AssetLoader.BomberPrefab == null)
            {
                IssaPluginPlugin.Log.LogInfo(
                    "[Bomber] Cannot spawn bomber visual; cannot find prefab."
                );
                return null;
            }

            IssaPluginPlugin.Log.LogInfo("[Bomber] Spawning bomber visual.");

            var go = Object.Instantiate(
                AssetLoader.BomberPrefab,
                spawnPos,
                Quaternion.LookRotation(direction, Vector3.up)
            );

            var flyComp = go.AddComponent<BomberFlyBehaviour>();
            flyComp.destination = exitPos;
            flyComp.speed = speed;

            return go;
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

        private static void SpawnRocket(
            PlayerInventory inventory,
            Vector3 position,
            Quaternion jitter
        )
        {
            if (!NetworkServer.active)
                return;

            Quaternion rotation = jitter * Quaternion.LookRotation(Vector3.down, Vector3.forward);

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

            ExplosionScaler.Register(rocket, Configuration.StealthBomberExplosionScale.Value);
        }

        /// Attached to the bomber prefab instance so it flies smoothly
        /// from spawn to destination independent of the rocket-drop coroutine.
        /// Promoted to internal so BomberNetworkBridge.RpcBomberShotDown can
        /// disable it before attaching BomberCrashBehaviour.
        internal class BomberFlyBehaviour : MonoBehaviour
        {
            public Vector3 destination;
            public float speed;

            private void Update()
            {
                if (!enabled)
                    return;

                transform.position = Vector3.MoveTowards(
                    transform.position,
                    destination,
                    speed * Time.deltaTime
                );

                if (Vector3.Distance(transform.position, destination) < 0.5f)
                {
                    StealthBomberItem.ActiveBomberVisual = null;
                    Destroy(gameObject);
                }
            }
        }
    }
}
