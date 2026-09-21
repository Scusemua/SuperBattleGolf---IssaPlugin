using System.Collections.Generic;
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
        /// Asks the server to launch a ball along <paramref name="direction"/>.
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

        /// <summary>
        /// Asks the server to launch a ball at the local player from TestFireDistance
        /// away. Bound to Cannon.TestFireAtSelfKey — for hit-reaction testing only.
        /// </summary>
        public static void ClientRequestTestFireAtSelf()
        {
            if (!NetworkClient.active)
                return;

            NetworkClient.Send(new CannonTestFireAtSelfMessage());
        }

        // ================================================================
        //  Server
        // ================================================================

        /// <summary>
        /// Spawns and launches one bowling ball from the shooter's barrel.
        /// Registered in NetworkManagerPatches.
        /// </summary>
        public void ServerHandleCannonShootMessage(Vector3 direction, int equippedSlotIndex)
        {
            if (!isServer)
                return;

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

            // Spawn at the rocket barrel tip, then nudge further along the shot so a
            // large bowling-ball collider is less likely to overlap the shooter even
            // before IgnoreCollision is applied in CannonBallBehavior.Start.
            Vector3 spawnPos =
                shooter.RightHandEquipmentSwitcher.transform.TransformPoint(
                    GameManager.ItemSettings.RocketLauncherLocalRocketPosition
                )
                + direction * 0.75f;

            ServerSpawnBall(shooter, inventory?.PlayerInfo ?? shooter, spawnPos, direction, ignoreThrower: true);
        }

        /// <summary>
        /// Debug: spawn a ball aimed at the requesting player from TestFireDistance away.
        /// Does not consume an item use. Registered in NetworkManagerPatches.
        /// </summary>
        public void ServerHandleTestFireAtSelfMessage()
        {
            if (!isServer)
                return;

            var target = GetComponent<PlayerInfo>();
            if (target == null)
                return;

            float distance = ModConfig.Cannon.TestFireDistance.Value;

            // Prefer the player's facing so the ball comes from "in front" of them.
            Vector3 away = target.transform.forward;
            away.y = 0f;
            if (away.sqrMagnitude < 0.0001f)
                away = Vector3.forward;
            away.Normalize();

            // Spawn in front, slightly above chest height, aimed back at the player.
            Vector3 aimPoint = target.transform.position + Vector3.up * 1.0f;
            Vector3 spawnPos = aimPoint + away * distance;
            Vector3 direction = (aimPoint - spawnPos).normalized;

            ServerSpawnBall(target, target, spawnPos, direction, ignoreThrower: false);
        }

        /// <summary>
        /// Shared spawn path for normal shots and the test-fire hotkey.
        /// </summary>
        private void ServerSpawnBall(
            PlayerInfo attributionPlayer,
            PlayerInfo throwerInfo,
            Vector3 spawnPos,
            Vector3 direction,
            bool ignoreThrower
        )
        {
            var prefab = AssetLoader.CannonBallPrefab;
            if (prefab == null)
            {
                IssaPluginPlugin.Log.LogWarning(
                    "[Cannon] CannonBallPrefab is null; cannot launch a bowling ball."
                );
                return;
            }

            if (!IsFinite(direction) || direction.sqrMagnitude < 0.0001f)
                return;

            direction = direction.normalized;

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
            behaviour.ThrowerInfo = throwerInfo ?? attributionPlayer;
            behaviour.ThrowerIgnoreDuration = ModConfig.Cannon.ThrowerIgnoreDuration.Value;
            behaviour.InitialVelocity = direction * ModConfig.Cannon.LaunchSpeed.Value;

            // Test-fire deliberately does not ignore the target — the whole point is
            // to hit them. Normal shots ignore the shooter for the configured duration.
            if (ignoreThrower && behaviour.ThrowerIgnoreDuration > 0f)
                behaviour.BeginIgnoreThrower();

            _serverLaunchedCannonBalls.RemoveAll(c => c == null);
            _serverLaunchedCannonBalls.Add(cannonBall);

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
