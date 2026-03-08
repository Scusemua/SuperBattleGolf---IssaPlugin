using System.Collections;
using System.Reflection;
using Mirror;
using UnityEngine;
using UnityEngine.InputSystem;

namespace IssaPlugin.Items
{
    public static class AC130Helpers
    {
        private static int _useIndex;
        
        // ----------------------------------------------------------------
        //  Helpers
        // ----------------------------------------------------------------
        public static Vector3 OrbitPosition(
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

        public static Vector3 OrbitTangent(float angleDeg)
        {
            // Derivative of OrbitPosition with respect to angle, normalised.
            float rad = angleDeg * Mathf.Deg2Rad;
            return new Vector3(-Mathf.Sin(rad), 0f, Mathf.Cos(rad)).normalized;
        }

        public static Vector3 ProjectAimToGround(Vector3 origin, Vector3 direction)
        {
            // Intersect the aim ray with y = 0 (sea-level ground plane).
            if (Mathf.Abs(direction.y) < 0.001f)
                return origin + direction * 500f;

            float t = -origin.y / direction.y;
            if (t < 0f)
                return origin + direction * 500f;

            return origin + direction * t;
        }

        public static Vector3 GetMapCentre(PlayerInventory inventory)
        {
            // Use the player's current position projected to ground as the
            // orbit centre so the gunship always circles the action.
            var pos = inventory.PlayerInfo.transform.position;
            return new Vector3(pos.x, 0f, pos.z);
        }

        public static void RestoreCamera(
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

        public static void SpawnRocket(
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
    } // end of class
} // end of namespace
