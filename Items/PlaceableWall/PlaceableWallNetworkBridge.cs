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
    public class PlaceableWallNetworkBridge : NetworkBehaviour
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

            NetworkClient.Send(
                new PlaceWallMessage
                {
                    RayOrigin = cam.transform.position,
                    RayDirection = cam.transform.forward,
                }
            );
        }

        // ── Server handler ───────────────────────────────────────────────

        public void ServerHandlePlacement(Vector3 rayOrigin, Vector3 rayDirection)
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

            // Reject if the placement centre is too close to the hole.
            if (!IsValidPlacement(placePos))
            {
                IssaPluginPlugin.Log.LogInfo(
                    $"[PlaceableWall] Placement at {placePos} rejected: too close to the hole."
                );
                return;
            }

            // Consume item only after all validation passes.
            ItemHelper.ConsumeEquippedItem(inventory);

            // Orient the wall so it faces back toward the placing player.
            // The wall runs perpendicular to the camera's XZ direction.
            Vector3 cameraXZ = Vector3.ProjectOnPlane(rayDirection, Vector3.up);
            if (cameraXZ.sqrMagnitude < 0.001f)
                cameraXZ = Vector3.forward; // straight-down look fallback
            Quaternion wallRotation = Quaternion.LookRotation(-cameraXZ.normalized, Vector3.up);

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

        private static bool IsValidPlacement(Vector3 position)
        {
            float minDist = Configuration.PlaceableWallMinHoleDistance.Value;
            if (minDist <= 0f)
                return true;

            var mainHole = GolfHoleManager.MainHole;
            if (mainHole == null)
                return true;

            Vector3 holePos = mainHole.transform.position;
            float xzDist = new Vector2(
                position.x - holePos.x,
                position.z - holePos.z
            ).magnitude;

            return xzDist >= minDist;
        }

        // ── Hole cleanup ─────────────────────────────────────────────────

        public void ServerHoleCleanup()
        {
            foreach (var wall in _activeWalls)
                if (wall != null)
                    NetworkServer.Destroy(wall);

            _activeWalls.Clear();

            IssaPluginPlugin.Log.LogInfo("[PlaceableWall] ServerHoleCleanup: all walls destroyed.");
        }
    }
}
