using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Mirror;
using UnityEngine;

namespace IssaPlugin.Items
{
    public static class PredatorMissileItem
    {
        public static readonly ItemType MissileItemType = (ItemType)102;
        private static int _missileUseIndex;

        internal static readonly HashSet<Rocket> ActiveMissileRockets = new();

        public static void GiveMissileToLocalPlayer()
        {
            ItemHelper.GiveItemToLocalPlayer(
                MissileItemType,
                Configuration.MissileUses.Value,
                "Missile"
            );
        }

        /// <summary>
        /// Runs entirely on the server. Spawns the rocket, notifies the
        /// firing client to begin steering, then waits for impact or timeout.
        /// </summary>
        public static IEnumerator ServerMissileRoutine(
            PlayerInventory inventory,
            MissileNetworkBridge bridge
        )
        {
            var playerInfo = inventory.PlayerInfo;
            var playerTransform = playerInfo.transform;

            // Consume the item server-side
            int slotIndex = inventory.EquippedItemIndex;
            if (slotIndex >= 0)
                ItemHelper.DecrementAndRemove(inventory, slotIndex);

            float altitude = Configuration.MissileAltitude.Value;
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
                yield break;
            }

            rocket.ServerInitialize(playerInfo, null, itemUseId);
            NetworkServer.Spawn(rocket.gameObject, (NetworkConnectionToClient)null);
            ActiveMissileRockets.Add(rocket);

            ExplosionScaler.Register(rocket, Configuration.PredatorMissileExplosionScale.Value);

            IssaPluginPlugin.Log.LogInfo($"[Missile] Launched at {spawnPos}.");

            // Give Mirror one frame to sync the new NetworkIdentity to clients
            yield return null;

            // Tell the firing client to start steering
            var rocketIdentity = rocket.GetComponent<NetworkIdentity>();
            bridge.TargetBeginSteering(bridge.connectionToClient, rocketIdentity);

            // Wait for rocket to die or timeout
            float elapsed = 0f;
            float timeout = Configuration.MissileTimeout.Value;

            while (rocket != null && rocket.gameObject != null && elapsed < timeout)
            {
                elapsed += Time.deltaTime;
                yield return null;
            }

            // Force explode if still alive at timeout
            if (rocket != null && rocket.gameObject != null)
                ServerExplode(rocket);

            ActiveMissileRockets.Remove(rocket);
            bridge.TargetEndSteering(bridge.connectionToClient);
            bridge.ServerClearSteering();
            IssaPluginPlugin.Log.LogInfo("[Missile] Server routine complete.");
        }

        public static void ServerExplode(Rocket rocket)
        {
            if (rocket == null)
                return;

            var explodeMethod = typeof(Rocket).GetMethod(
                "ServerExplode",
                BindingFlags.NonPublic | BindingFlags.Instance
            );

            if (explodeMethod != null)
                explodeMethod.Invoke(rocket, new object[] { rocket.transform.position });
            else
                IssaPluginPlugin.Log.LogError("[Missile] Could not find ServerExplode method.");
        }
    }
}
