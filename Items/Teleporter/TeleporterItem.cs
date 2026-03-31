using System.Collections;
using Mirror;
using UnityEngine;
using UnityEngine.InputSystem;

namespace IssaPlugin.Items
{
    /// <summary>
    /// Client-side logic for the Teleporter item.
    ///
    /// When used the local player enters a targeting phase that is visually and
    /// structurally identical to the Stealth Bomber's path-selection UI:
    ///  - The camera pans to a top-down overhead view.
    ///  - A glowing marker disc follows WASD movement and shows where the player
    ///    will land.
    ///  - Left-click / Enter confirms; right-click / Space cancels.
    ///  - On confirm the client sends <see cref="TeleporterUseMessage"/> to the
    ///    server, which performs the authoritative teleport and broadcasts VFX.
    ///
    /// Static fields are safe here for the same reason as StealthBomberItem:
    /// only one local player's input is ever processed on a given client.
    /// </summary>
    public static class TeleporterItem
    {
        private static bool _isTargeting;
        private static bool _targetingCancelled;
        private static readonly int TerrainLayer = LayerMask.NameToLayer("Terrain");
        private static readonly RaycastHit[] raycastHitBuffer = new RaycastHit[30];

        public static bool IsTargeting => _isTargeting;

        /// <summary>
        /// Signals the targeting coroutine to exit immediately.
        /// Called on hole transitions so the camera and input are restored cleanly.
        /// </summary>
        public static void CancelTargeting() => _targetingCancelled = true;

        // ────────────────────────────────────────────────────────────────
        //  Entry point — called from TeleporterItemDefinition.OnUse
        // ────────────────────────────────────────────────────────────────

        public static IEnumerator TeleporterUseRoutine(PlayerInventory inventory)
        {
            if (_isTargeting)
                yield break;

            int equippedIndex = inventory.EquippedItemIndex;

            Vector3? destination = null;
            yield return RunTargetingPhase(inventory, result => destination = result);

            if (destination == null)
            {
                IssaPluginPlugin.Log.LogInfo("[Teleporter] Targeting cancelled.");
                yield break;
            }

            NetworkClient.Send(
                new TeleporterUseMessage
                {
                    Destination = destination.Value,
                    EquippedIndex = equippedIndex,
                }
            );
        }

        // ────────────────────────────────────────────────────────────────
        //  Targeting phase
        // ────────────────────────────────────────────────────────────────

        private static IEnumerator RunTargetingPhase(
            PlayerInventory inventory,
            System.Action<Vector3?> onComplete
        )
        {
            _isTargeting = true;
            _targetingCancelled = false;
            InputManager.Controls.Gameplay.Disable();

            OrbitCameraModule orbitModule = null;
            CameraModuleController.TryGetOrbitModule(out orbitModule);

            float savedPitch = orbitModule?.Pitch ?? 0f;
            float savedYaw = orbitModule?.Yaw ?? 0f;
            bool savedDisablePhysics = false;

            // Place the marker above the player's current position, snapped to terrain.
            var playerPos = inventory.PlayerInfo.transform.position;
            float snappedY = SampleTerrainY(playerPos.x, playerPos.z, playerPos.y);
            var markerStartPos = new Vector3(playerPos.x, snappedY, playerPos.z);

            var pivotGo = new GameObject("TeleporterTargetPivot");
            pivotGo.transform.position = playerPos;

            var markerGo = CreateMarkerVisual(markerStartPos);

            float currentDistanceAddition = 25f;
            float zoomSpeed = Configuration.BomberTargetingZoomSpeed.Value; // reuse bomber zoom speed

            if (orbitModule != null)
            {
                savedDisablePhysics = orbitModule.disablePhysics;
                orbitModule.SetSubject(pivotGo.transform);
                orbitModule.SetPitch(88f);
                orbitModule.SetDistanceAddition(currentDistanceAddition);
                orbitModule.disablePhysics = true;
                orbitModule.ForceUpdateModule();
            }

            yield return null; // let camera settle for one frame

            float moveSpeed = Configuration.TeleporterTargetMoveSpeed.Value;
            bool confirmed = false;
            bool cancelled = false;

            while (!confirmed && !cancelled && !_targetingCancelled)
            {
                var keyboard = Keyboard.current;
                var mouse = Mouse.current;

                if (keyboard == null)
                {
                    yield return null;
                    continue;
                }

                // ── Movement ────────────────────────────────────────────────
                HandleMarkerMovement(keyboard, orbitModule, moveSpeed, markerGo, pivotGo);

                // ── Zoom ─────────────────────────────────────────────────────
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

                // ── Confirm / Cancel ─────────────────────────────────────────
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

            Vector3? result = confirmed ? markerGo.transform.position : (Vector3?)null;

            Object.Destroy(markerGo);
            Object.Destroy(pivotGo);
            _isTargeting = false;

            RestoreCamera(orbitModule, savedPitch, savedYaw, savedDisablePhysics);
            InputManager.Controls.Gameplay.Enable();

            onComplete(result);
        }

        // ────────────────────────────────────────────────────────────────
        //  Input helpers
        // ────────────────────────────────────────────────────────────────

        private static void HandleMarkerMovement(
            Keyboard keyboard,
            OrbitCameraModule orbitModule,
            float moveSpeed,
            GameObject markerGo,
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

            // Apply XZ movement then snap Y to the terrain surface.
            Vector3 pos = markerGo.transform.position + worldMove;
            pos.y = SampleTerrainY(pos.x, pos.z, markerGo.transform.position.y);
            markerGo.transform.position = pos;

            // Keep pivot aligned horizontally with the marker so the camera
            // follows without drifting vertically.
            pivotGo.transform.position = new Vector3(pos.x, pivotGo.transform.position.y, pos.z);
        }

        /// <summary>
        /// Casts a ray straight down from well above the given XZ coordinate and
        /// returns the Y of the first ground hit, offset slightly upward so the
        /// marker disc sits on top of the surface rather than clipping into it.
        ///
        /// Falls back to <paramref name="fallbackY"/> when nothing is hit (e.g. the
        /// marker is dragged over a void or out-of-bounds area).
        ///
        /// Performance note: a single Physics.Raycast per frame is negligible.
        /// The layer mask excludes players and triggers so we only hit solid world
        /// geometry. Tighten the mask further if your project has a dedicated
        /// "Terrain" or "Ground" layer.
        /// </summary>
        private static float SampleTerrainY(float x, float z, float fallbackY)
        {
            // Cast from 2000 units above so even tall geometry is captured.
            const float castOriginHeight = 2000f;
            const float markerSurfaceOffset = 0.5f; // sits this far above the hit point

            var origin = new Vector3(x, castOriginHeight, z);
            int numHits = Physics.RaycastNonAlloc(
                origin,
                Vector3.down,
                raycastHitBuffer,
                castOriginHeight * 2f,
                GameManager.LayerSettings.PlayerGroundableMask,
                QueryTriggerInteraction.Ignore
            );
            if (numHits > 0)
            {
                var hit = raycastHitBuffer[0];
                return hit.point.y + markerSurfaceOffset;
            }

            // Nothing hit — keep the marker at its current height so it doesn't
            // snap to zero over a void.
            return fallbackY;
        }

        // ────────────────────────────────────────────────────────────────
        //  Marker visual — a glowing disc with a crosshair ring
        // ────────────────────────────────────────────────────────────────

        private static GameObject CreateMarkerVisual(Vector3 center)
        {
            var root = new GameObject("TeleporterTargetMarker");
            // Y is already terrain-snapped by the caller; place the root exactly there.
            root.transform.position = center;

            float radius = Configuration.TeleporterMarkerRadius.Value;

            // Main filled disc (a flat cylinder in Unity).
            CreateMarkerPart(
                root.transform,
                "MarkerDisc",
                Vector3.zero,
                new Vector3(radius * 2f, 0.15f, radius * 2f),
                new Color(0.2f, 0.6f, 1f, 0.35f)
            );

            // Outer ring — slightly larger, thinner, more opaque.
            CreateMarkerPart(
                root.transform,
                "MarkerRing",
                new Vector3(0f, 0.08f, 0f),
                new Vector3(radius * 2f + 1.5f, 0.1f, radius * 2f + 1.5f),
                new Color(0.4f, 0.8f, 1f, 0.55f)
            );

            // Four small directional indicators (N / S / E / W ticks).
            float tickLen = radius * 0.55f;
            float tickThick = Mathf.Max(radius * 0.07f, 0.4f);
            var tickColor = new Color(0.5f, 0.9f, 1f, 0.7f);

            CreateMarkerPart(
                root.transform,
                "TickN",
                new Vector3(0f, 0.2f, radius + tickLen * 0.5f),
                new Vector3(tickThick, 0.2f, tickLen),
                tickColor
            );

            CreateMarkerPart(
                root.transform,
                "TickS",
                new Vector3(0f, 0.2f, -(radius + tickLen * 0.5f)),
                new Vector3(tickThick, 0.2f, tickLen),
                tickColor
            );

            CreateMarkerPart(
                root.transform,
                "TickE",
                new Vector3(radius + tickLen * 0.5f, 0.2f, 0f),
                new Vector3(tickLen, 0.2f, tickThick),
                tickColor
            );

            CreateMarkerPart(
                root.transform,
                "TickW",
                new Vector3(-(radius + tickLen * 0.5f), 0.2f, 0f),
                new Vector3(tickLen, 0.2f, tickThick),
                tickColor
            );

            return root;
        }

        private static GameObject CreateMarkerPart(
            Transform parent,
            string name,
            Vector3 localPos,
            Vector3 localScale,
            Color color
        )
        {
            // Cylinder for the disc; Cube for the ticks.
            var go = name.StartsWith("Tick")
                ? GameObject.CreatePrimitive(PrimitiveType.Cube)
                : GameObject.CreatePrimitive(PrimitiveType.Cylinder);

            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = localScale;

            // Remove collider — purely visual.
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
                // Draw through geometry so it's always visible on terrain.
                mat.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.Always);
                mat.SetInt("_ZWrite", 0);
                mat.renderQueue = 3100;
                renderer.material = mat;
            }

            return go;
        }

        // ────────────────────────────────────────────────────────────────
        //  Camera restore — identical to StealthBomberItem.RestoreCamera
        // ────────────────────────────────────────────────────────────────

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
    }
}
