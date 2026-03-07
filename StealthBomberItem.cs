using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using IssaPlugin.Patches;
using Mirror;
using UnityEngine;

namespace IssaPlugin.Items
{
    public static class StealthBomberItem
    {
        public static readonly ItemType BomberItemType = (ItemType)101;

        private static bool _isBombing;
        private static int _bomberUseIndex;

        public static void GiveBomberToLocalPlayer()
        {
            var inventory = GameManager.LocalPlayerInventory;
            if (inventory == null)
            {
                IssaPluginPlugin.Log.LogWarning("[Bomber] No local player inventory.");
                return;
            }

            if (NetworkServer.active)
            {
                bool added = InventoryPatches.DirectAddCustomItem(
                    inventory,
                    BomberItemType,
                    Configuration.BomberUses.Value
                );
                if (!added)
                    IssaPluginPlugin.Log.LogWarning(
                        "[Bomber] Failed to add bomber (inventory full?)."
                    );
            }
            else
            {
                var cmdAddItem = typeof(PlayerInventory).GetMethod(
                    "CmdAddItem",
                    BindingFlags.NonPublic | BindingFlags.Instance
                );

                if (cmdAddItem != null)
                {
                    cmdAddItem.Invoke(inventory, new object[] { BomberItemType });
                    IssaPluginPlugin.Log.LogInfo("[Bomber] Requested bomber via server command.");
                }
                else
                {
                    IssaPluginPlugin.Log.LogError("[Bomber] Could not find CmdAddItem method.");
                }
            }
        }

        private static void SetCurrentItemUse(PlayerInventory inventory, ItemUseType itemUseType)
        {
            var setCurrentItemUse = typeof(PlayerInventory).GetMethod(
                "SetCurrentItemUse",
                BindingFlags.NonPublic | BindingFlags.Instance
            );
            if (setCurrentItemUse != null)
            {
                setCurrentItemUse.Invoke(inventory, new object[] { itemUseType });
                IssaPluginPlugin.Log.LogInfo(
                    "[Bomber] Setting item usage to " + itemUseType.ToString() + "."
                );
            }
            else
            {
                IssaPluginPlugin.Log.LogError("[Bomber] Could not find SetCurrentItemUse method.");
            }
        }

        private static void DecrementUseFromSlotAt(PlayerInventory inventory, int slotIndex)
        {
            var decrementUseFromSlotAt = typeof(PlayerInventory).GetMethod(
                "DecrementUseFromSlotAt",
                BindingFlags.NonPublic | BindingFlags.Instance
            );
            if (decrementUseFromSlotAt != null)
            {
                decrementUseFromSlotAt.Invoke(inventory, new object[] { slotIndex });
            }
            else
            {
                IssaPluginPlugin.Log.LogError(
                    "[Bomber] Could not find DecrementUseFromSlotAt method."
                );
            }
        }

        private static void RemoveIfOutOfUses(PlayerInventory inventory, int slotIndex)
        {
            var removeIfOutOfUses = typeof(PlayerInventory).GetMethod(
                "RemoveIfOutOfUses",
                BindingFlags.NonPublic | BindingFlags.Instance
            );
            if (removeIfOutOfUses != null)
            {
                removeIfOutOfUses.Invoke(inventory, new object[] { slotIndex });
            }
            else
            {
                IssaPluginPlugin.Log.LogError("[Bomber] Could not find RemoveIfOutOfUses method.");
            }
        }

        private static void ThrowUsedItemForAllClients(PlayerInventory inventory)
        {
            var throwUsedItemForAllClients = typeof(PlayerInventory).GetMethod(
                "ThrowUsedItemForAllClients",
                BindingFlags.NonPublic | BindingFlags.Instance
            );
            if (throwUsedItemForAllClients != null)
            {
                throwUsedItemForAllClients.Invoke(
                    inventory,
                    new object[] { ThrownUsedItemType.RocketLauncher, false, default(Vector3) }
                );
            }
            else
            {
                IssaPluginPlugin.Log.LogError(
                    "[Bomber] Could not find ThrowUsedItemForAllClients method."
                );
            }
        }

        private static void MarkThrownItem(PlayerInventory inventory, int hand)
        {
            var markThrownItem = typeof(PlayerInventory).GetMethod(
                "MarkThrownItem",
                BindingFlags.NonPublic | BindingFlags.Instance
            );
            if (markThrownItem != null)
            {
                markThrownItem.Invoke(inventory, new object[] { hand });
            }
            else
            {
                IssaPluginPlugin.Log.LogError("[Bomber] Could not find MarkThrownItem method.");
            }
        }

        public static IEnumerator BomberRunRoutine(PlayerInventory inventory)
        {
            if (_isBombing)
                yield break;

            SetCurrentItemUse(inventory, ItemUseType.Regular);

            Vector3 startPos,
                endPos;
            if (!TryGetBomberPath(out startPos, out endPos))
            {
                IssaPluginPlugin.Log.LogWarning("[Bomber] Could not determine tee/hole positions.");
                _isBombing = false;
                SetCurrentItemUse(inventory, ItemUseType.None);
                yield break;
            }

            _isBombing = true;
            DecrementUseFromSlotAt(inventory, inventory.EquippedItemIndex);
            RemoveIfOutOfUses(inventory, inventory.EquippedItemIndex);
            SetCurrentItemUse(inventory, ItemUseType.None);

            // Return immediately to update the player's inventory.
            yield return new WaitForSeconds(0.01f);

            yield return new WaitForSeconds(Configuration.BomberWaitTime.Value);

            float altitude = Configuration.BomberAltitude.Value;
            float speed = Configuration.BomberSpeed.Value;
            float rocketInterval = Configuration.BomberRocketInterval.Value;
            int rocketCount = Configuration.BomberRocketCount.Value;

            startPos.y += altitude;
            endPos.y += altitude;
            Vector3 direction = (endPos - startPos).normalized;
            float totalDistance = Vector3.Distance(startPos, endPos);

            float rocketSpacing = totalDistance / (rocketCount + 1);
            var rocketDropPoints = new List<float>(rocketCount);
            for (int i = 1; i <= rocketCount; i++)
                rocketDropPoints.Add(rocketSpacing * i);

            IssaPluginPlugin.Log.LogInfo(
                $"[Bomber] Run started: {rocketCount} rockets over {totalDistance:F0}m at altitude {altitude:F0}m"
            );

            float distanceTravelled = 0f;
            int rocketsDropped = 0;

            while (distanceTravelled < totalDistance && rocketsDropped < rocketCount)
            {
                float step = speed * Time.deltaTime;
                distanceTravelled += step;

                while (
                    rocketsDropped < rocketCount
                    && distanceTravelled >= rocketDropPoints[rocketsDropped]
                )
                {
                    Vector3 dropPos = startPos + direction * rocketDropPoints[rocketsDropped];

                    float spread = Configuration.BomberSpread.Value;
                    Vector3 offset = new Vector3(
                        Random.Range(-spread, spread),
                        0f,
                        Random.Range(-spread, spread)
                    );

                    RequestRocketSpawn(inventory, dropPos + offset);
                    rocketsDropped++;

                    yield return new WaitForSeconds(rocketInterval);
                }

                yield return null;
            }

            IssaPluginPlugin.Log.LogInfo(
                $"[Bomber] Run complete. {rocketsDropped} rockets dropped."
            );
            _isBombing = false;

            yield break;
        }

        /// <summary>
        /// Spawns a downward-facing rocket directly on the server,
        /// bypassing CmdInformShotRocket and its anti-cheat rate limiter.
        /// </summary>
        private static void RequestRocketSpawn(PlayerInventory inventory, Vector3 position)
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

        private static bool TryGetBomberPath(out Vector3 start, out Vector3 end)
        {
            start = end = Vector3.zero;

            var hole = GolfHoleManager.MainHole;
            if (hole == null)
                return false;

            end = hole.transform.position;

            var tees = GolfTeeManager.ActiveTeeingPlaforms;
            if (tees == null || tees.Count == 0)
                return false;

            float maxDist = 0f;
            foreach (var tee in tees)
            {
                float dist = Vector3.Distance(tee.transform.position, end);
                if (dist > maxDist)
                {
                    maxDist = dist;
                    start = tee.transform.position;
                }
            }

            return maxDist > 1f;
        }
    }
}
