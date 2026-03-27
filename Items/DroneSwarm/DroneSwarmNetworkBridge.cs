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
    ///   Validates the item use, consumes it, spawns DroneCount drone GameObjects,
    ///   attaches DroneBehaviour to each, and tracks them for cleanup when the session
    ///   ends or a hole transition occurs.
    ///   Sends overlay begin / drone-died messages to the owning client only via
    ///   connectionToClient.Send() (TargetRpc replacement for unwoven mods).
    ///
    /// CLIENT SIDE (owning player only):
    ///   Sends DroneSwarmSummonMessage when the player activates the item.
    ///   Hosts the static handlers that route incoming server broadcasts to the
    ///   correct components on all clients.
    ///
    /// Multiple players may have concurrent drone swarm sessions — there is no
    /// global lock. Each DroneSwarmNetworkBridge tracks only its summoner's drones.
    /// </summary>
    public class DroneSwarmNetworkBridge : NetworkBridgeBase
    {
        // ── Server state ──────────────────────────────────────────────────────

        private readonly List<GameObject> _activeDrones = new List<GameObject>();
        private bool _serverSessionActive;
        private Coroutine _serverTimeout;

        private static int _useIndex;

        private static int NextUseIndex() => Interlocked.Increment(ref _useIndex);

        // ── Mirror lifecycle ──────────────────────────────────────────────────

        public override void OnStopServer()
        {
            if (_serverSessionActive)
            {
                IssaPluginPlugin.Log.LogInfo(
                    "[DroneSwarm] Player disconnected during active session — cleaning up."
                );
                ForceServerCleanup();
            }
        }

        // ── Server entry point ────────────────────────────────────────────────

        /// <summary>
        /// Called on the server when the client sends DroneSwarmSummonMessage.
        /// Validates equipped item, consumes it, and spawns drones.
        /// </summary>
        public void ServerSummonDrones()
        {
            if (_serverSessionActive)
            {
                IssaPluginPlugin.Log.LogWarning(
                    "[DroneSwarm] Session already active for this player — ignoring."
                );
                return;
            }

            var inventory = GetComponent<PlayerInventory>();
            if (inventory == null)
            {
                IssaPluginPlugin.Log.LogError("[DroneSwarm] No PlayerInventory on bridge object.");
                return;
            }

            var equipped = inventory.GetEffectivelyEquippedItem(true);
            if (equipped != ItemRegistry.DroneSwarmItemType)
            {
                IssaPluginPlugin.Log.LogWarning(
                    "[DroneSwarm] Player does not have Drone Swarm item equipped."
                );
                return;
            }

            if (AssetLoader.DronePrefab == null)
            {
                IssaPluginPlugin.Log.LogError("[DroneSwarm] DronePrefab not loaded.");
                return;
            }

            ItemHelper.ConsumeEquippedItem(inventory);
            ItemWarningBroadcaster.Broadcast(
                inventory.PlayerInfo.PlayerId.PlayerName,
                ItemRegistry.DroneSwarmItemType,
                "Drone Swarm",
                senderNetId: netId
            );

            int droneCount = (int)Configuration.DroneCount.Value;
            float altitude = Configuration.DroneAltitude.Value;
            float radius = Configuration.DroneWanderRadius.Value;

            // Wander centre = player centroid at ground level, lifted to altitude.
            Vector3 wanderCenter = ComputeOrbitCenter(altitude);

            _serverSessionActive = true;
            _serverTimeout = StartCoroutine(ServerMaxDurationRoutine());

            for (int i = 0; i < droneCount; i++)
                SpawnOneDrone(wanderCenter, radius, inventory.PlayerInfo);

            connectionToClient.Send(new DroneSwarmOverlayBeginMessage { DroneCount = droneCount });

            IssaPluginPlugin.Log.LogInfo(
                $"[DroneSwarm] Spawned {droneCount} drone(s) for "
                    + $"{inventory.PlayerInfo.PlayerId.PlayerName}."
            );
        }

        // ── Server internals ──────────────────────────────────────────────────

        private void SpawnOneDrone(Vector3 wanderCenter, float wanderRadius, PlayerInfo summoner)
        {
            // Scatter each drone at a random position within the wander area so
            // they don't all spawn on top of each other.
            float spawnAngle = Random.Range(0f, 360f) * Mathf.Deg2Rad;
            float spawnRadius = Random.Range(wanderRadius * 0.2f, wanderRadius * 0.8f);
            float altOffset = Random.Range(
                -Configuration.DroneAltitudeVariance.Value,
                Configuration.DroneAltitudeVariance.Value
            );
            Vector3 spawnPos = new Vector3(
                wanderCenter.x + Mathf.Sin(spawnAngle) * spawnRadius,
                wanderCenter.y + altOffset,
                wanderCenter.z + Mathf.Cos(spawnAngle) * spawnRadius
            );

            var droneGo = Object.Instantiate(
                AssetLoader.DronePrefab,
                spawnPos,
                Quaternion.identity
            );

            // Build a unique ItemUseId per drone so kills are properly attributed.
            var itemUseId = new ItemUseId(
                summoner.PlayerId.Guid,
                NextUseIndex(),
                ItemType.RocketLauncher
            );

            var behaviour = droneGo.AddComponent<DroneBehaviour>();
            behaviour.WanderCenter = wanderCenter;
            behaviour.WanderRadius = wanderRadius;
            behaviour.WanderSpeed = Configuration.DroneWanderSpeed.Value;
            behaviour.WanderTurnRate = Configuration.DroneWanderTurnRate.Value;
            behaviour.AltitudeVariance = Configuration.DroneAltitudeVariance.Value;
            behaviour.CircleTime = Random.Range(
                Configuration.DroneCircleTimeMin.Value,
                Configuration.DroneCircleTimeMax.Value
            );
            behaviour.DiveSpeed = Configuration.DroneDiveSpeed.Value;
            behaviour.DiveAcceleration = Configuration.DroneDiveAcceleration.Value;
            behaviour.HomingStopDistance = Configuration.DroneHomingStopDistance.Value;
            behaviour.ArrivalRadius = Configuration.DroneArrivalRadius.Value;
            behaviour.ExplosionScale = Configuration.DroneExplosionScale.Value;
            behaviour.ThrowerInfo = summoner;
            behaviour.ItemUseId = itemUseId;
            behaviour.FriendlyFire = Configuration.DroneFriendlyFire.Value;
            behaviour.AttackFinishedPlayers = Configuration.DroneAttackFinishedPlayers.Value;

            // Spawn AFTER full setup so Mirror calls Start() post-Spawn.
            NetworkServer.Spawn(droneGo);

            _activeDrones.Add(droneGo);
            StartCoroutine(WatchDrone(droneGo));
        }

        private IEnumerator WatchDrone(GameObject droneGo)
        {
            while (droneGo != null)
                yield return null;

            _activeDrones.Remove(droneGo);

            // Notify owning client that one drone is gone (HUD counter update).
            if (connectionToClient != null)
                connectionToClient.Send(new DroneDiedClientMessage());

            // End session automatically once all drones are gone.
            if (_serverSessionActive && _activeDrones.Count == 0)
            {
                IssaPluginPlugin.Log.LogInfo("[DroneSwarm] All drones destroyed — session ending.");
                EndServerSession();
            }
        }

        private IEnumerator ServerMaxDurationRoutine()
        {
            yield return new WaitForSeconds(Configuration.DroneSwarmMaxSessionDuration.Value);

            if (_serverSessionActive)
            {
                IssaPluginPlugin.Log.LogInfo("[DroneSwarm] Session timed out.");
                EndServerSession();
            }
        }

        private void EndServerSession()
        {
            if (!_serverSessionActive)
                return;

            _serverSessionActive = false;

            if (_serverTimeout != null)
            {
                StopCoroutine(_serverTimeout);
                _serverTimeout = null;
            }

            // Copy then clear BEFORE destroying so WatchDrone coroutines that wake
            // from forced destruction see an empty list and do not re-enter.
            var toDestroy = new List<GameObject>(_activeDrones);
            _activeDrones.Clear();

            foreach (var drone in toDestroy)
            {
                if (drone != null)
                    NetworkServer.Destroy(drone);
            }

            NetworkServer.SendToAll(new DroneSwarmSessionEndedMessage());

            if (connectionToClient != null)
                connectionToClient.Send(new DroneSwarmOverlayEndMessage());

            IssaPluginPlugin.Log.LogInfo("[DroneSwarm] Server session ended.");
        }

        // ── Hole-transition cleanup ───────────────────────────────────────────

        public override void ServerHoleCleanup()
        {
            if (_serverSessionActive)
                ForceServerCleanup();
        }

        public override void ClientHoleCleanup() { }

        private void ForceServerCleanup()
        {
            if (_serverTimeout != null)
            {
                StopCoroutine(_serverTimeout);
                _serverTimeout = null;
            }

            foreach (var drone in _activeDrones)
            {
                if (drone != null)
                    NetworkServer.Destroy(drone);
            }
            _activeDrones.Clear();

            _serverSessionActive = false;

            if (connectionToClient != null)
                connectionToClient.Send(new DroneSwarmOverlayEndMessage());
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        /// <summary>
        /// Returns the centroid of all connected players at the given altitude.
        /// Falls back to world origin if no players are found.
        /// </summary>
        private static Vector3 ComputeOrbitCenter(float altitude)
        {
            var players = Object.FindObjectsByType<PlayerInventory>(FindObjectsSortMode.None);
            if (players.Length == 0)
                return new Vector3(0f, altitude, 0f);

            Vector3 sum = Vector3.zero;
            foreach (var p in players)
                sum += p.transform.position;

            Vector3 centroid = sum / players.Length;
            centroid.y = altitude;
            return centroid;
        }

        // ── Static NetworkClient message handlers ─────────────────────────────
        // Registered in NetworkManagerPatches.RegisterNetworkMessages().

        /// <summary>
        /// Received by all clients when a drone detonates.
        /// Spawns the custom explosion VFX locally (not networked — each client owns its copy).
        /// </summary>
        public static void HandleDroneExploded(DroneExplodedMessage msg)
        {
            if (AssetLoader.DroneExplosionVfxPrefab == null)
                return;

            Object.Instantiate(
                AssetLoader.DroneExplosionVfxPrefab,
                msg.Position,
                Quaternion.identity
            );
        }

        /// <summary>Received by all clients when the swarm session ends.</summary>
        public static void HandleSessionEnded(DroneSwarmSessionEndedMessage msg)
        {
            IssaPluginPlugin.Log.LogInfo("[DroneSwarm] Session ended (all clients).");
        }

        // ── Per-client overlay messages (owning client only) ──────────────────

        public static void HandleOverlayBegin(DroneSwarmOverlayBeginMessage msg)
        {
            IssaPlugin.Overlays.DroneSwarmOverlay.Instance?.SetActive(true, msg.DroneCount);
        }

        public static void HandleDroneDied(DroneDiedClientMessage msg)
        {
            IssaPlugin.Overlays.DroneSwarmOverlay.Instance?.DecrementDroneCount();
        }

        public static void HandleOverlayEnd(DroneSwarmOverlayEndMessage msg)
        {
            IssaPlugin.Overlays.DroneSwarmOverlay.Instance?.Deactivate();
        }
    }
}
