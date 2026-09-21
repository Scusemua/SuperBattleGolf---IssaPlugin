using System.Collections.Generic;
using HarmonyLib;
using IssaPlugin.Items.Cannon;
using IssaPlugin.Network;
using Mirror;
using UnityEngine;

namespace IssaPlugin.Items
{
    public class CannonNetworkBridge : NetworkBridgeBase
    {
        // Every bowling ball launched by any player this hole, tracked so the server can
        // despawn them at the hole transition. Static because cleanup is global and
        // a launched bowling ball outlives the interaction with its shooter's bridge.
        private static readonly List<GameObject> _serverLaunchedCannonBalls = new();

        // ================================================================
        //  Client → Server
        // ================================================================

        /// <summary>
        /// Asks the server to launch a cart along <paramref name="direction"/>.
        /// Called by Cannon.Fire on the shooter's client.
        /// </summary>
        public void ClientRequestLaunch(Vector3 direction, int equippedSlotIndex) =>
            NetworkClient.Send(
                new CannonShootMessage
                {
                    Direction = direction,
                    EquippedSlotIndex = equippedSlotIndex,
                }
            );

        // ================================================================
        //  Server
        // ================================================================

        /// <summary>
        /// Spawns and launches one bowling ball. Registered in NetworkManagerPatches.
        /// </summary>
        public void ServerHandleCannonShootMessage(Vector3 direction, int equippedSlotIndex)
        {
            if (!isServer)
                return;

            // Reject a malformed or hostile direction rather than launching a cart
            // with NaN velocity, which would corrupt the physics scene for everyone.
            if (!IsFinite(direction) || direction.sqrMagnitude < 0.0001f)
                return;

            direction = direction.normalized;

            var shooter = GetComponent<PlayerInfo>();
            if (shooter == null)
                return;

            var inventory = GetComponent<PlayerInventory>();

            // Anti-cheat validation, for REMOTE senders only.
            //
            // It cannot be applied to the host's own shot. On a host the shooting client
            // is the server, so PlayerInventory.DecrementUseFromSlotAt and RemoveItemAt
            // take their isServer branch and write straight to the `slots` SyncList —
            // the same list any validation would read. By the time this handler runs,
            // the use that authorised the shot is already spent (and the final use has
            // emptied the slot entirely), so ANY inventory-based check rejects the
            // host's own valid shots. There is no pre-consumption state left to inspect.
            //
            // That is safe to skip: the host IS the server, so its requests are
            // authoritative by definition and there is nothing to spoof. Remote clients
            // still go through the full check, and their inventory is untouched at this
            // point because their consumption only reaches the server as a separate
            // Cmd — which is exactly the state this validation is meant to police.
            bool fromRemoteClient =
                connectionToClient != null && connectionToClient != NetworkServer.localConnection;

            if (fromRemoteClient)
            {
                if (
                    inventory == null
                    || ItemRegistry.GetItemTypeAtSlot(inventory, equippedSlotIndex)
                        != ItemRegistry.CannonItemType
                )
                {
                    IssaPluginPlugin.Log.LogWarning(
                        "[Cannon] Launch request from a player without the launcher equipped."
                    );
                    return;
                }
            }

            var prefab = AssetLoader.CannonBallPrefab;
            if (prefab == null)
            {
                IssaPluginPlugin.Log.LogWarning(
                    "[Cannon] CannonBallPrefab is null; cannot launch a bowling ball."
                );
                return;
            }

            // Spawn at the rocket barrel tip, then nudge further along the shot so a
            // large bowling-ball collider is less likely to overlap the shooter even
            // before IgnoreCollision is applied in CannonBallBehavior.Start.
            Vector3 spawnPos =
                shooter.RightHandEquipmentSwitcher.transform.TransformPoint(
                    GameManager.ItemSettings.RocketLauncherLocalRocketPosition
                )
                + direction * 0.75f;

            // Face the ball along its flight path, kept upright relative to world up.
            Quaternion spawnRot = Quaternion.LookRotation(direction, Vector3.up);

            GameObject cannonBall = Object.Instantiate(prefab, spawnPos, spawnRot);
            if (cannonBall == null)
                return;

            // Moving golf balls use DynamicBallLayer so they collide with terrain
            // additions (buildings/props). Leaving the prefab on Default misses those
            // contacts while still hitting players, carts, and terrain.
            SetLayerRecursive(cannonBall, GameManager.LayerSettings.DynamicBallLayer);
            EnsureSolidColliders(cannonBall);

            // The server drives this ball's flight; without this the host's local
            // client would own the transform and overwrite the launch velocity.
            ServerAuthoritativeTransform.Apply(cannonBall, "CannonBall");

            if (cannonBall.GetComponentInChildren<NetworkTransformBase>(true) == null)
            {
                IssaPluginPlugin.Log.LogWarning(
                    "[Cannon] bowling_ball.prefab has no NetworkTransform; "
                        + "remote clients will not see the ball move. Add NetworkTransformReliable "
                        + "(or equivalent) to the prefab in the asset bundle."
                );
            }

            NetworkServer.Spawn(cannonBall);

            var behaviour = cannonBall.AddComponent<CannonBallBehavior>();
            behaviour.ThrowerInfo = inventory.PlayerInfo;
            behaviour.InitialVelocity = direction * ModConfig.Cannon.LaunchSpeed.Value;
            // Apply IgnoreCollision immediately — waiting for Start() can let the
            // first FixedUpdate resolve a self-hit if the ball overlaps the shooter.
            behaviour.BeginIgnoreThrower();

            // Drop entries whose ball is already gone (despawned by lifetime, driven
            // out of bounds, or destroyed by the game) so a long hole with heavy
            // launcher use does not grow this list without bound.
            _serverLaunchedCannonBalls.RemoveAll(c => c == null);
            _serverLaunchedCannonBalls.Add(cannonBall);

            // The timer lives on the ball, not on this bridge, so it keeps running if
            // the shooter disconnects while their ball is still in the air.
            float lifetime = ModConfig.Cannon.BowlingBallLifetime.Value;
            if (lifetime > 0f)
                cannonBall.AddComponent<LaunchedCannonBallDespawner>().Initialize(lifetime);
        }

        private static bool IsFinite(Vector3 v) =>
            !float.IsNaN(v.x)
            && !float.IsNaN(v.y)
            && !float.IsNaN(v.z)
            && !float.IsInfinity(v.x)
            && !float.IsInfinity(v.y)
            && !float.IsInfinity(v.z);

        private static void SetLayerRecursive(GameObject go, int layer)
        {
            go.layer = layer;
            for (int i = 0; i < go.transform.childCount; i++)
                SetLayerRecursive(go.transform.GetChild(i).gameObject, layer);
        }

        /// <summary>
        /// Hardens the prefab's colliders for rigidbody vs mesh world geometry.
        /// A non-convex MeshCollider on a dynamic body will not collide with other
        /// MeshColliders (typical for buildings), which matches the reported miss.
        /// </summary>
        private static void EnsureSolidColliders(GameObject go)
        {
            foreach (var col in go.GetComponentsInChildren<Collider>(true))
            {
                col.isTrigger = false;
                if (col is MeshCollider mesh)
                    mesh.convex = true;
            }
        }

        // ================================================================
        //  Cleanup
        // ================================================================

        /// <summary>
        /// Destroys every bowling ball launched this hole. Runs on the first bridge to be
        /// cleaned up; the list is static and shared, so later calls are no-ops.
        /// </summary>
        private static void ServerDestroyAllLaunchedCannonBalls()
        {
            foreach (var cannonBall in _serverLaunchedCannonBalls)
            {
                if (cannonBall != null)
                    NetworkServer.Destroy(cannonBall);
            }

            _serverLaunchedCannonBalls.Clear();
        }

        public override void ServerHoleCleanup()
        {
            ServerDestroyAllLaunchedCannonBalls();
        }

        public override void ClientHoleCleanup()
        {
            CannonItem.ResetFiringState();
        }

        public override void OnStopServer()
        {
            ServerDestroyAllLaunchedCannonBalls();
        }
    }
}
