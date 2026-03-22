using System.Collections.Generic;
using Mirror;
using UnityEngine;

namespace IssaPlugin.Items
{
    /// <summary>Stateless utility methods shared by the Harrier Jet subsystem.</summary>
    public static class HarrierItem
    {
        private static int _useIndex;

        // ----------------------------------------------------------------
        //  Rocket spawning
        // ----------------------------------------------------------------

        /// <summary>
        /// Spawns the game's RocketPrefab from <paramref name="fromPosition"/> aimed
        /// at <paramref name="targetPosition"/>, with random angular jitter applied.
        /// Must be called on the server.
        /// </summary>
        public static void SpawnRocketAtTarget(
            PlayerInventory inventory,
            Vector3 fromPosition,
            Vector3 targetPosition
        )
        {
            if (!NetworkServer.active)
                return;

            Vector3 direction = (targetPosition - fromPosition).normalized;

            // Apply random angular jitter so rockets aren't perfectly accurate.
            float jitterDeg = Configuration.HarrierRocketJitter.Value;
            if (jitterDeg > 0f)
            {
                // Build two vectors perpendicular to the fire direction.
                Vector3 perp1 = Vector3.Cross(direction, Vector3.up);
                if (perp1.sqrMagnitude < 0.01f)
                    perp1 = Vector3.Cross(direction, Vector3.right);
                perp1.Normalize();
                Vector3 perp2 = Vector3.Cross(direction, perp1).normalized;

                float angle = Random.value * 360f * Mathf.Deg2Rad;
                float spread = Mathf.Tan(jitterDeg * Mathf.Deg2Rad) * Random.value;
                direction = (direction
                    + (Mathf.Cos(angle) * perp1 + Mathf.Sin(angle) * perp2) * spread
                ).normalized;
            }

            Quaternion rotation = direction != Vector3.zero
                ? Quaternion.LookRotation(direction)
                : Quaternion.identity;

            _useIndex++;
            var itemUseId = new ItemUseId(
                inventory.PlayerInfo.PlayerId.Guid,
                _useIndex,
                ItemType.RocketLauncher
            );

            var rocket = Object.Instantiate(
                GameManager.ItemSettings.RocketPrefab,
                fromPosition,
                rotation
            );

            if (rocket == null)
            {
                IssaPluginPlugin.Log.LogError("[Harrier] Rocket did not instantiate.");
                return;
            }

            // Prevent the game's lock-on system from assigning homing to this rocket.
            rocket.gameObject.AddComponent<CustomSpawnedRocket>();

            rocket.ServerInitialize(inventory.PlayerInfo, null, itemUseId);
            NetworkServer.Spawn(rocket.gameObject, (NetworkConnectionToClient)null);

            ExplosionScaler.Register(rocket, Configuration.HarrierExplosionScale.Value);
        }

        // ----------------------------------------------------------------
        //  Targeting
        // ----------------------------------------------------------------

        /// <summary>
        /// Returns all players eligible to be targeted by the Harrier.
        /// When <paramref name="friendlyFireEnabled"/> is false, the player who
        /// activated the item is excluded from the list.
        /// </summary>
        public static List<PlayerInventory> GetValidTargets(
            PlayerInfo throwerInfo,
            bool friendlyFireEnabled
        )
        {
            var result = new List<PlayerInventory>();

            foreach (var inv in Object.FindObjectsByType<PlayerInventory>(FindObjectsSortMode.None))
            {
                if (!friendlyFireEnabled
                    && inv.PlayerInfo.PlayerId.Guid == throwerInfo.PlayerId.Guid)
                    continue;

                result.Add(inv);
            }

            return result;
        }

        /// <summary>
        /// Picks a random valid target and fires one rocket at them from
        /// <paramref name="fromPosition"/>.
        /// Returns <c>true</c> if a target was found and a rocket was fired.
        /// </summary>
        public static bool FireAtRandomTarget(
            PlayerInventory inventory,
            Vector3 fromPosition,
            bool friendlyFireEnabled
        )
        {
            var targets = GetValidTargets(inventory.PlayerInfo, friendlyFireEnabled);
            if (targets.Count == 0)
                return false;

            var target = targets[Random.Range(0, targets.Count)];
            SpawnRocketAtTarget(inventory, fromPosition, target.transform.position);
            return true;
        }
    }
}
