using System.Collections;
using Mirror;
using UnityEngine;
using UnityEngine.InputSystem;

namespace IssaPlugin.Items
{
    /// <summary>
    /// Client-side drop-zone targeting for the UFO Abduction item.
    ///
    /// After the server validates a lock-on and sends TargetAcquiredMessage, the
    /// wielder's client enters an overhead targeting phase identical in structure to
    /// TeleporterItem — WASD to move marker, scroll to zoom, click/Enter to confirm,
    /// Space/RMB to cancel.
    ///
    /// On confirm:  sends UfoAbductionDropoffSelectedMessage to server.
    /// On cancel:   sends UfoAbductionDropoffCancelledMessage to server.
    /// On external cancel (hole transition / disconnect): exits silently — server's
    /// timeout coroutine handles the abort and broadcasts SessionAbortedMessage.
    /// </summary>
    public static class UfoAbductionTargeting
    {
        private static bool _isSelectingDropoff;
        private static bool _selectionCancelled;

        public static bool IsSelectingDropoff => _isSelectingDropoff;

        /// Sets the external cancel flag. Does NOT send any message to the server.
        public static void CancelSelection() => _selectionCancelled = true;

        // ── Entry point — started via movement.StartCoroutine from HandleTargetAcquired ──

        public static IEnumerator BeginDropoffSelectionRoutine(Vector3 victimPos)
        {
            if (_isSelectingDropoff)
                yield break;

            yield return RunSelectionPhase(victimPos);
        }

        // ── Selection coroutine ──────────────────────────────────────────────

        private static IEnumerator RunSelectionPhase(Vector3 victimPos)
        {
            _isSelectingDropoff = true;
            _selectionCancelled = false;
            InputManager.Controls.Gameplay.Disable();

            OrbitCameraModule orbitModule = null;
            CameraModuleController.TryGetOrbitModule(out orbitModule);

            float savedPitch = orbitModule?.Pitch ?? 0f;
            float savedYaw = orbitModule?.Yaw ?? 0f;
            bool savedDisablePhysics = false;

            // Marker starts at victim's position, snapped to terrain.
            float snappedY = ItemHelper.SampleTerrainY(victimPos.x, victimPos.z, victimPos.y);
            var markerStartPos = new Vector3(victimPos.x, snappedY, victimPos.z);

            var pivotGo = new GameObject("UfoAbductionDropoffPivot");
            pivotGo.transform.position = victimPos;

            var markerGo = CreateMarkerVisual(markerStartPos);

            float currentDistanceAddition = 25f;
            float zoomSpeed = ModConfig.StealthBomber.TargetingZoomSpeed.Value;

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

            float moveSpeed = ModConfig.Teleporter.TargetMoveSpeed.Value;
            bool confirmed = false;
            bool cancelled = false;

            while (!confirmed && !cancelled && !_selectionCancelled)
            {
                var keyboard = Keyboard.current;
                var mouse = Mouse.current;

                if (keyboard == null)
                {
                    yield return null;
                    continue;
                }

                HandleMarkerMovement(keyboard, orbitModule, moveSpeed, markerGo, pivotGo);

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

            Vector3 markerPos = markerGo.transform.position;

            Object.Destroy(markerGo);
            Object.Destroy(pivotGo);
            _isSelectingDropoff = false;

            RestoreCamera(orbitModule, savedPitch, savedYaw, savedDisablePhysics);
            InputManager.Controls.Gameplay.Enable();

            if (confirmed)
            {
                IssaPluginPlugin.Log.LogInfo(
                    $"[UfoAbduction] Targeting: drop zone confirmed at {markerPos}."
                );
                NetworkClient.Send(
                    new UfoAbductionDropoffSelectedMessage { Destination = markerPos }
                );
            }
            else if (cancelled)
            {
                IssaPluginPlugin.Log.LogInfo("[UfoAbduction] Targeting: wielder cancelled.");
                NetworkClient.Send(new UfoAbductionDropoffCancelledMessage());
            }
            // else: _selectionCancelled — external cancel; server handles abort via timeout/cleanup
        }

        // ── Input helpers ────────────────────────────────────────────────────

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

            Vector3 pos = markerGo.transform.position + worldMove;
            pos.y = ItemHelper.SampleTerrainY(pos.x, pos.z, markerGo.transform.position.y);
            markerGo.transform.position = pos;

            pivotGo.transform.position = new Vector3(pos.x, pivotGo.transform.position.y, pos.z);
        }

        // ── Marker visual — alien/green theme ────────────────────────────────

        private static GameObject CreateMarkerVisual(Vector3 center)
        {
            var root = new GameObject("UfoAbductionDropoffMarker");
            root.transform.position = center;

            float radius = ModConfig.Teleporter.MarkerRadius.Value;

            CreateMarkerPart(
                root.transform,
                "MarkerDisc",
                Vector3.zero,
                new Vector3(radius * 2f, 0.15f, radius * 2f),
                new Color(0.2f, 1f, 0.4f, 0.35f)
            );

            CreateMarkerPart(
                root.transform,
                "MarkerRing",
                new Vector3(0f, 0.08f, 0f),
                new Vector3(radius * 2f + 1.5f, 0.1f, radius * 2f + 1.5f),
                new Color(0.4f, 1f, 0.6f, 0.55f)
            );

            float tickLen = radius * 0.55f;
            float tickThick = Mathf.Max(radius * 0.07f, 0.4f);
            var tickColor = new Color(0.5f, 1f, 0.7f, 0.7f);

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
            var go = name.StartsWith("Tick")
                ? GameObject.CreatePrimitive(PrimitiveType.Cube)
                : GameObject.CreatePrimitive(PrimitiveType.Cylinder);

            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = localScale;

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

        // ── Camera restore ───────────────────────────────────────────────────

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
