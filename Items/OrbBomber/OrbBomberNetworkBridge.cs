using System.Threading;
using IssaPlugin.Network;
using Mirror;
using UnityEngine;

namespace IssaPlugin.Items
{
    public class OrbBomberNetworkBridge : NetworkBridgeBase
    {
        private static int _useIndex;
        private PlayerInventory _inventory;

        private static int NextUseIndex() => Interlocked.Increment(ref _useIndex);

        private void Awake() => _inventory = GetComponent<PlayerInventory>();

        public void ServerHandleRequest(uint targetNetId, int equippedSlotIndex)
        {
            if (!isServer || _inventory == null)
                return;

            if (
                ItemRegistry.GetItemTypeAtSlot(_inventory, equippedSlotIndex)
                != ItemRegistry.OrbBomberItemType
            )
            {
                IssaPluginPlugin.Log.LogWarning(
                    "[OrbBomber] ServerHandleRequest: item not at expected slot."
                );
                return;
            }

            if (AssetLoader.OrbBomberPrefab == null)
            {
                IssaPluginPlugin.Log.LogError("[OrbBomber] Prefab not loaded.");
                return;
            }

            if (!NetworkServer.spawned.TryGetValue(targetNetId, out var targetIdentity))
            {
                IssaPluginPlugin.Log.LogWarning(
                    $"[OrbBomber] ServerHandleRequest: target netId {targetNetId} not found."
                );
                return;
            }

            var targetInventory = targetIdentity.GetComponent<PlayerInventory>();
            var targetInfo = targetInventory?.PlayerInfo;
            var summoner = _inventory.PlayerInfo;
            if (summoner == null)
            {
                IssaPluginPlugin.Log.LogWarning(
                    "[OrbBomber] ServerHandleRequest: summoner has no PlayerInfo."
                );
                return;
            }

            var chaseTarget = OrbBomberBehaviour.ChaseTransform(targetInfo);
            if (chaseTarget == null)
            {
                IssaPluginPlugin.Log.LogWarning(
                    ModConfig.OrbBomber.ChaseGolfBall.Value
                        ? "[OrbBomber] ServerHandleRequest: target has no GolfBall."
                        : "[OrbBomber] ServerHandleRequest: target has no player."
                );
                return;
            }

            ItemHelper.ConsumeItemAtSlot(_inventory, equippedSlotIndex);
            ItemWarningBroadcaster.Broadcast(
                summoner.PlayerId.PlayerName,
                ItemRegistry.OrbBomberItemType,
                "Orb Bomber",
                senderNetId: netId
            );

            int count = Mathf.Clamp(
                Mathf.RoundToInt(ModConfig.OrbBomber.SpawnCount.Value),
                OrbBomberConfig.MinSpawnCount,
                OrbBomberConfig.MaxSpawnCount
            );
            // Sample every spot before any orb exists, so a later ray cannot
            // land on an orb that was just spawned.
            float ringAngle = Random.Range(0f, Mathf.PI * 2f);
            var spots = new SpawnSpot[count];
            for (int i = 0; i < count; i++)
            {
                float angle = ringAngle + Mathf.PI * 2f * i / count;
                Vector3 spot = PickSpawnPosition(chaseTarget.position, angle);
                spots[i] = new SpawnSpot
                {
                    Position = spot,
                    FoundGround = TrySampleGround(spot, out float ground),
                    GroundY = ground,
                };
            }

            for (int i = 0; i < count; i++)
            {
                SpawnOrb(
                    spots[i].Position,
                    spots[i].FoundGround,
                    spots[i].GroundY,
                    chaseTarget.position.y,
                    summoner,
                    targetInfo,
                    targetNetId
                );
            }

            IssaPluginPlugin.Log.LogInfo(
                $"[OrbBomber] Spawned {count} for {summoner.PlayerId.PlayerName} targeting netId={targetNetId}."
            );
        }

        private struct SpawnSpot
        {
            public Vector3 Position;
            public float GroundY;
            public bool FoundGround;
        }

        private static void SpawnOrb(
            Vector3 spawnPos,
            bool foundGround,
            float groundY,
            float fallbackY,
            PlayerInfo summoner,
            PlayerInfo targetInfo,
            uint targetNetId
        )
        {
            var orb = Object.Instantiate(
                AssetLoader.OrbBomberPrefab,
                spawnPos,
                Quaternion.identity
            );

            PrepareSpawnedOrb(orb);

            var setup = orb.GetComponent<OrbBomberClientSetup>();
            float radius = setup != null ? setup.BaseRadius : 0.5f;
            spawnPos.y = (foundGround ? groundY : fallbackY) + radius;

            orb.transform.position = spawnPos;

            var rb = orb.GetComponent<Rigidbody>();
            if (rb == null)
                rb = orb.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;
            rb.position = spawnPos;

            var behaviour = orb.AddComponent<OrbBomberBehaviour>();
            behaviour.ThrowerInfo = summoner;
            behaviour.TargetInfo = targetInfo;
            behaviour.VictimNetId = targetNetId;
            behaviour.ItemUseId = new ItemUseId(
                summoner.PlayerId.Guid,
                NextUseIndex(),
                ItemType.RocketLauncher,
                false
            );

            NetworkServer.Spawn(orb);
        }

        private static Vector3 PickSpawnPosition(Vector3 ballPosition, float angle)
        {
            float minR = ModConfig.OrbBomber.MinSpawnRadius.Value;
            float maxR = ModConfig.OrbBomber.MaxSpawnRadius.Value;
            if (maxR < minR)
                (minR, maxR) = (maxR, minR);
            minR = Mathf.Max(1f, minR);
            maxR = Mathf.Max(minR, maxR);

            float radius = Random.Range(minR, maxR);
            return ballPosition
                + new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
        }

        private static void PrepareSpawnedOrb(GameObject orb)
        {
            ServerAuthoritativeTransform.Apply(orb, "OrbBomber");

            foreach (var netTransform in orb.GetComponentsInChildren<NetworkTransformBase>(true))
                netTransform.syncScale = false;

            // Destroy is deferred to the end of the frame, which is late enough
            // for NetworkRigidbody to fight the first MovePosition. Disable it now.
            foreach (var component in orb.GetComponentsInChildren<Component>(true))
            {
                if (component == null || !component.GetType().Name.StartsWith("NetworkRigidbody"))
                    continue;

                if (component is Behaviour behaviour)
                    behaviour.enabled = false;
                Object.Destroy(component);
            }
        }

        private static bool TrySampleGround(Vector3 position, out float groundY)
        {
            groundY = position.y;
            int mask = GameManager.LayerSettings != null
                ? GameManager.LayerSettings.PlayerGroundableMask
                : Physics.DefaultRaycastLayers;

            // Start just above this spot, at the target's altitude, so a roof
            // over the target is not the first hit. The high ray covers a hill
            // that rises above that altitude.
            var near = new Vector3(position.x, position.y + 6f, position.z);
            if (OrbBomberBehaviour.TryFirstGround(near, 80f, mask, out groundY))
                return true;

            var high = new Vector3(position.x, position.y + 2000f, position.z);
            return OrbBomberBehaviour.TryFirstGround(high, 4000f, mask, out groundY);
        }

        public override void ServerHoleCleanup() => OrbBomberBehaviour.ServerCleanupAll();

        public override void ClientHoleCleanup() => OrbBomberOverlay.Instance?.ForceClose();
    }
}
