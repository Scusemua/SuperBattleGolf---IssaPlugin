using System.Collections;
using System.Collections.Generic;
using System.Threading;
using IssaPlugin.Network;
using Mirror;
using UnityEngine;

namespace IssaPlugin.Items
{
    /// <summary>
    /// Attached to every player object via NetworkBridgePatches.
    ///
    /// SERVER SIDE:
    ///   Validates item use, consumes it, spawns one hunter drone, attaches
    ///   HunterDroneBehaviour, and tracks the drone for cleanup.
    ///   Also handles HunterDroneShotMessage (bullet hit reported by a client).
    ///
    /// CLIENT SIDE (local player only):
    ///   Sends HunterDroneLaunchMessage when the player activates the item.
    ///   Hosts static handlers for server-broadcast messages (VFX).
    ///
    /// Up to MaxActiveDrones drones per player may be active simultaneously.
    /// </summary>
    public class HunterDroneNetworkBridge : NetworkBridgeBase
    {
        // ── Server state ──────────────────────────────────────────────────────

        private readonly List<GameObject> _activeDrones = new();

        private static int _useIndex;

        private static int NextUseIndex() => Interlocked.Increment(ref _useIndex);

        // ── Mirror lifecycle ──────────────────────────────────────────────────

        public override void OnStopServer()
        {
            if (_activeDrones.Count > 0)
            {
                IssaPluginPlugin.Log.LogInfo(
                    "[HunterDrone] Player disconnected during active session — cleaning up."
                );
                ForceServerCleanup();
            }
        }

        // ── Server entry points ───────────────────────────────────────────────

        /// <summary>
        /// Called on the server when the client sends HunterDroneLaunchMessage.
        /// </summary>
        public void ServerLaunchDrone(Vector3 aimPoint)
        {
            if (!isServer)
                return;

            var inventory = GetComponent<PlayerInventory>();
            if (inventory == null)
            {
                IssaPluginPlugin.Log.LogError("[HunterDrone] No PlayerInventory on bridge object.");
                return;
            }

            if (inventory.GetEffectivelyEquippedItem(true) != ItemRegistry.HunterDroneItemType)
            {
                IssaPluginPlugin.Log.LogWarning(
                    "[HunterDrone] Player does not have Hunter Drone item equipped."
                );
                return;
            }

            if (AssetLoader.HunterDronePrefab == null)
            {
                IssaPluginPlugin.Log.LogError(
                    "[HunterDrone] HunterDronePrefab not loaded — rebuild the asset bundle "
                        + "to include hunter_drone.prefab."
                );
                return;
            }

            int maxActive = (int)ModConfig.HunterDrone.MaxActiveDrones.Value;
            if (_activeDrones.Count >= maxActive)
            {
                IssaPluginPlugin.Log.LogInfo(
                    $"[HunterDrone] Already at the limit of {maxActive} active drone(s) — ignoring."
                );
                return;
            }

            ItemHelper.ConsumeEquippedItem(inventory);
            ItemWarningBroadcaster.Broadcast(
                inventory.PlayerInfo.PlayerId.PlayerName,
                ItemRegistry.HunterDroneItemType,
                "Hunter Drone",
                senderNetId: netId
            );

            SpawnDrone(inventory.PlayerInfo, aimPoint);
        }

        /// <summary>
        /// Global server handler for <see cref="HunterDroneShotMessage"/>.
        /// Looks up the drone directly in <c>NetworkServer.spawned</c> so it works
        /// regardless of which player fired the shot — routing via the shooter's own
        /// bridge would always fail because that bridge has no active drone of its own.
        /// Registered in NetworkManagerPatches without a per-connection bridge lookup.
        /// </summary>
        public static void ServerHandleDroneShot(uint droneNetId)
        {
            if (!NetworkServer.active)
                return;

            if (
                !NetworkServer.spawned.TryGetValue(droneNetId, out var identity)
                || identity == null
            )
            {
                IssaPluginPlugin.Log.LogWarning(
                    $"[HunterDrone] Shot report for netId {droneNetId} — not found in spawned."
                );
                return;
            }

            identity.GetComponent<HunterDroneBehaviour>()?.ShootDown();
        }

        // ── Server internals ──────────────────────────────────────────────────

        private void SpawnDrone(PlayerInfo summoner, Vector3 aimPoint)
        {
            // Spawn slightly above and in front of the player so the drone doesn't
            // immediately collide with the thrower.
            Vector3 forward = summoner.transform.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.001f)
                forward = Vector3.forward;
            forward.Normalize();

            Vector3 spawnPos = summoner.transform.position + forward * 1.5f + Vector3.up * 2.0f;

            var droneGo = Object.Instantiate(
                AssetLoader.HunterDronePrefab,
                spawnPos,
                summoner.transform.rotation
            );

            // Put the drone on the HittablesLayer so bullet raycasts (GunHittablesMask)
            // can detect it. We set all child objects as well because Unity does not
            // propagate layer changes automatically.
            SetLayerRecursive(droneGo, GameManager.LayerSettings.HittablesLayer);

            var itemUseId = new ItemUseId(
                summoner.PlayerId.Guid,
                NextUseIndex(),
                ItemType.RocketLauncher,
                false
            );

            var behaviour = droneGo.AddComponent<HunterDroneBehaviour>();
            behaviour.LaunchSpeed = ModConfig.HunterDrone.LaunchSpeed.Value;
            behaviour.Acceleration = ModConfig.HunterDrone.Acceleration.Value;
            behaviour.HomingStopDistance = ModConfig.HunterDrone.HomingStopDistance.Value;
            behaviour.ArrivalRadius = ModConfig.HunterDrone.ArrivalRadius.Value;
            behaviour.ExplosionScale = ModConfig.HunterDrone.ExplosionScale.Value;
            behaviour.ThrowerInfo = summoner;
            behaviour.ItemUseId = itemUseId;
            behaviour.MaxFlightDistance = ModConfig.HunterDrone.MaxFlightDistance.Value;
            behaviour.MaxSpeed = ModConfig.HunterDrone.MaxSpeed.Value;
            behaviour.FriendlyFire = ModConfig.HunterDrone.FriendlyFire.Value;
            behaviour.AttackFinishedPlayers = ModConfig.HunterDrone.AttackFinishedPlayers.Value;
            behaviour.ArmDelay = ModConfig.HunterDrone.ArmDelay.Value;
            behaviour.SetFallbackAimPoint(aimPoint);

            NetworkServer.Spawn(droneGo);

            _activeDrones.Add(droneGo);
            StartCoroutine(WatchDrone(droneGo));

            IssaPluginPlugin.Log.LogInfo(
                $"[HunterDrone] Spawned drone for "
                    + $"{summoner.PlayerId.PlayerName} at {spawnPos:F1}."
            );
        }

        private IEnumerator WatchDrone(GameObject droneGo)
        {
            while (droneGo != null)
                yield return null;

            // RemoveAll(null) is used instead of Remove(droneGo) because Unity's equality
            // operator on a destroyed object (fake-null) can match other destroyed objects,
            // making Remove unreliable when multiple drones die in the same frame.
            _activeDrones.RemoveAll(go => go == null);
            IssaPluginPlugin.Log.LogInfo(
                $"[HunterDrone] Drone destroyed — {_activeDrones.Count} still active."
            );
        }

        // ── Hole / server cleanup ─────────────────────────────────────────────

        public override void ServerHoleCleanup()
        {
            if (_activeDrones.Count > 0)
                ForceServerCleanup();
        }

        public override void ClientHoleCleanup() { }

        private void ForceServerCleanup()
        {
            // Stop all WatchDrone coroutines first so none of them fire RemoveAll
            // after we clear the list below.
            StopAllCoroutines();

            foreach (var drone in _activeDrones)
            {
                if (drone != null)
                    NetworkServer.Destroy(drone);
            }

            _activeDrones.Clear();
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static void SetLayerRecursive(GameObject go, int layer)
        {
            go.layer = layer;
            for (int i = 0; i < go.transform.childCount; i++)
                SetLayerRecursive(go.transform.GetChild(i).gameObject, layer);
        }

        // ── Static NetworkClient message handlers ─────────────────────────────
        // Registered in NetworkManagerPatches.RegisterNetworkMessages().

        /// <summary>
        /// Received by all clients when the hunter drone detonates.
        /// Spawns the local-only explosion VFX.
        /// </summary>
        public static void HandleDroneExploded(HunterDroneExplodedMessage msg)
        {
            if (AssetLoader.DroneExplosionVfxPrefab == null)
                return;

            Object.Instantiate(
                AssetLoader.DroneExplosionVfxPrefab,
                msg.Position,
                Quaternion.identity
            );
        }
    }
}
