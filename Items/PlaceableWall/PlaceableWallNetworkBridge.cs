using System.Collections.Generic;
using Mirror;
using UnityEngine;

namespace IssaPlugin.Items
{
    /// Attached to every player object via NetworkBridgePatches.
    ///
    /// Client side:  Reads the camera ray and sends PlaceWallMessage.
    /// Server side:  Validates placement, checks hole proximity, consumes the
    ///               item, and spawns the wall GameObject.
    public class PlaceableWallNetworkBridge : NetworkBridgeBase
    {
        // Server-side list of walls spawned by this player.
        // Internal so PlaceableWallBehaviour can remove itself when destroyed.
        internal readonly List<GameObject> _activeWalls = new List<GameObject>();

        // ── Client → Server ──────────────────────────────────────────────

        public void ClientPlace()
        {
            var cam = Camera.main;
            if (cam == null)
            {
                IssaPluginPlugin.Log.LogWarning(
                    "[PlaceableWall] No main camera for placement ray."
                );
                return;
            }

            // Pick up the accumulated R-key rotation from the preview component if present.
            float extraYaw = GetComponent<PlaceableWallPlacementPreview>()?.CurrentRotationOffset ?? 0f;

            NetworkClient.Send(
                new PlaceWallMessage
                {
                    RayOrigin = cam.transform.position,
                    RayDirection = cam.transform.forward,
                    ExtraYawDegrees = extraYaw,
                }
            );
        }

        // ── Server handler ───────────────────────────────────────────────

        public void ServerHandlePlacement(Vector3 rayOrigin, Vector3 rayDirection, float extraYawDegrees = 0f)
        {
            var inventory = GetComponent<PlayerInventory>();
            if (inventory == null)
                return;

            if (inventory.GetEffectivelyEquippedItem(true) != ItemRegistry.PlaceableWallItemType)
            {
                IssaPluginPlugin.Log.LogWarning(
                    "[PlaceableWall] Player does not have PlaceableWall equipped."
                );
                return;
            }

            if (AssetLoader.WallPrefab == null)
            {
                IssaPluginPlugin.Log.LogError("[PlaceableWall] Wall prefab not loaded.");
                return;
            }

            // Raycast from the camera position along the camera's forward direction
            // to find the ground surface where the wall should be placed.
            float maxDist = Configuration.PlaceableWallMaxPlacementDistance.Value;
            if (
                !Physics.Raycast(
                    rayOrigin,
                    rayDirection,
                    out RaycastHit hit,
                    maxDist,
                    ItemHelper.GroundLayerMask,
                    QueryTriggerInteraction.Ignore
                )
            )
            {
                IssaPluginPlugin.Log.LogInfo(
                    "[PlaceableWall] Placement raycast found no ground — ignoring."
                );
                return;
            }

            Vector3 placePos = hit.point;

            // Compute final rotation before validation so we can use it for the
            // player-overlap OverlapBox (same box the server will actually spawn).
            Vector3 cameraXZ = Vector3.ProjectOnPlane(rayDirection, Vector3.up);
            if (cameraXZ.sqrMagnitude < 0.001f)
                cameraXZ = Vector3.forward; // straight-down look fallback
            Quaternion baseRotation = Quaternion.LookRotation(-cameraXZ.normalized, Vector3.up);
            Quaternion wallRotation = baseRotation * Quaternion.Euler(0f, extraYawDegrees, 0f);

            // Reject placement if it would cover the hole or overlap any player.
            if (!IsValidPlacement(placePos, wallRotation))
            {
                IssaPluginPlugin.Log.LogInfo(
                    $"[PlaceableWall] Placement at {placePos} rejected (hole or player overlap)."
                );
                return;
            }

            // Consume item only after all validation passes.
            ItemHelper.ConsumeEquippedItem(inventory);

            var wallGo = Object.Instantiate(AssetLoader.WallPrefab, placePos, wallRotation);
            if (wallGo == null)
            {
                IssaPluginPlugin.Log.LogError("[PlaceableWall] Wall prefab failed to instantiate.");
                return;
            }

            foreach (Transform child in wallGo.transform)
            {
                if (child.GetComponent<Rigidbody>() != null)
                {
                    child.gameObject.AddComponent<PlaceableWallDestructionBehaviour>();
                }
            }

            NetworkServer.Spawn(wallGo);

            var behaviour = wallGo.AddComponent<PlaceableWallBehaviour>();
            behaviour.OwnerBridge = this;

            _activeWalls.Add(wallGo);

            IssaPluginPlugin.Log.LogInfo(
                $"[PlaceableWall] Wall placed at {placePos} by "
                    + $"{inventory.PlayerInfo?.PlayerId.PlayerName ?? "unknown"}."
            );
        }

        // ── Server → All Clients message handler (static) ───────────────

        public static void HandleWallDestroyed(WallDestroyedMessage msg)
        {
            VfxManager.PlayPooledVfxLocalOnly(
                VfxType.RocketLauncherRocketExplosion,
                msg.DestroyPosition,
                Quaternion.identity,
                Vector3.one * 0.6f
            );

            CameraModuleController.Shake(
                GameManager.CameraGameplaySettings.RocketExplosionScreenshakeSettings,
                msg.DestroyPosition
            );
        }

        // ── Placement validation ─────────────────────────────────────────

        private static bool IsValidPlacement(Vector3 position, Quaternion rotation)
        {
            // ── Hole distance check ──────────────────────────────────────
            float minDist = Configuration.PlaceableWallMinHoleDistance.Value;
            if (minDist > 0f)
            {
                var mainHole = GolfHoleManager.MainHole;
                if (mainHole != null)
                {
                    Vector3 holePos = mainHole.transform.position;
                    float xzDist = new Vector2(
                        position.x - holePos.x,
                        position.z - holePos.z
                    ).magnitude;
                    if (xzDist < minDist)
                        return false;
                }
            }

            // ── Player overlap check ─────────────────────────────────────
            // Use the wall prefab's scale to size the OverlapBox, matching the
            // preview component's check so the server and client agree.
            Vector3 scale =
                AssetLoader.WallPrefab != null
                    ? AssetLoader.WallPrefab.transform.localScale
                    : new Vector3(4f, 3f, 0.3f);
            Vector3 halfExtents = scale * 0.45f;
            Vector3 boxCenter = position + Vector3.up * halfExtents.y;

            var overlaps = Physics.OverlapBox(
                boxCenter,
                halfExtents,
                rotation,
                Physics.AllLayers,
                QueryTriggerInteraction.Ignore
            );

            foreach (var col in overlaps)
            {
                if (col.GetComponentInParent<PlayerMovement>() != null)
                    return false;
            }

            return true;
        }

        // ── Hole cleanup ─────────────────────────────────────────────────

        public override void ServerHoleCleanup()
        {
            foreach (var wall in _activeWalls)
                if (wall != null)
                    NetworkServer.Destroy(wall);

            _activeWalls.Clear();

            IssaPluginPlugin.Log.LogInfo("[PlaceableWall] ServerHoleCleanup: all walls destroyed.");
        }

        // Runs on client
        public override void ClientHoleCleanup() { }
    }
}
