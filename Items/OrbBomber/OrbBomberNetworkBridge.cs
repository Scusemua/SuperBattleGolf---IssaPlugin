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

            Vector3 spawnPos = PickSpawnPosition(chaseTarget.position);
            // Sample before the orb exists so the ray cannot hit the orb and
            // plant it on top of itself.
            bool foundGround = TrySampleGround(spawnPos, out float groundY);
            var orb = Object.Instantiate(
                AssetLoader.OrbBomberPrefab,
                spawnPos,
                Quaternion.identity
            );

            PrepareSpawnedOrb(orb);

            var setup = orb.GetComponent<OrbBomberClientSetup>();
            float radius = setup != null ? setup.BaseRadius : 0.5f;
            spawnPos.y = (foundGround ? groundY : chaseTarget.position.y) + radius;

            orb.transform.position = spawnPos;

            var rb = orb.GetComponent<Rigidbody>();
            if (rb == null)
                rb = orb.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;
            rb.position = spawnPos;

            var itemUseId = new ItemUseId(
                summoner.PlayerId.Guid,
                NextUseIndex(),
                ItemType.RocketLauncher,
                false
            );

            var behaviour = orb.AddComponent<OrbBomberBehaviour>();
            behaviour.ThrowerInfo = summoner;
            behaviour.TargetInfo = targetInfo;
            behaviour.VictimNetId = targetNetId;
            behaviour.ItemUseId = itemUseId;

            NetworkServer.Spawn(orb);

            IssaPluginPlugin.Log.LogInfo(
                $"[OrbBomber] Spawned for {summoner.PlayerId.PlayerName} targeting netId={targetNetId}."
            );
        }

        private static Vector3 PickSpawnPosition(Vector3 ballPosition)
        {
            float minR = ModConfig.OrbBomber.MinSpawnRadius.Value;
            float maxR = ModConfig.OrbBomber.MaxSpawnRadius.Value;
            if (maxR < minR)
                (minR, maxR) = (maxR, minR);
            minR = Mathf.Max(1f, minR);
            maxR = Mathf.Max(minR, maxR);

            float radius = Random.Range(minR, maxR);
            float angle = Random.Range(0f, Mathf.PI * 2f);
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
            var origin = new Vector3(position.x, position.y + 2000f, position.z);
            int mask = GameManager.LayerSettings != null
                ? GameManager.LayerSettings.PlayerGroundableMask
                : Physics.DefaultRaycastLayers;

            if (
                Physics.Raycast(
                    origin,
                    Vector3.down,
                    out RaycastHit hit,
                    4000f,
                    mask,
                    QueryTriggerInteraction.Ignore
                )
            )
            {
                groundY = hit.point.y;
                return true;
            }

            return false;
        }

        public override void ServerHoleCleanup() => OrbBomberBehaviour.ServerCleanupAll();

        public override void ClientHoleCleanup() => OrbBomberOverlay.Instance?.ForceClose();
    }
}
