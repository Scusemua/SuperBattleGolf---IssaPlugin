using System.Collections;
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
    /// Only one drone per player may be active at a time.
    /// </summary>
    public class HunterDroneNetworkBridge : NetworkBridgeBase
    {
        // ── Server state ──────────────────────────────────────────────────────

        private GameObject _activeDroneGo;
        private bool _serverSessionActive;
        private Coroutine _watchCoroutine;

        private static int _useIndex;

        private static int NextUseIndex() => Interlocked.Increment(ref _useIndex);

        // ── Mirror lifecycle ──────────────────────────────────────────────────

        public override void OnStopServer()
        {
            if (_serverSessionActive)
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

            if (_serverSessionActive)
            {
                IssaPluginPlugin.Log.LogWarning(
                    "[HunterDrone] A drone is already active for this player — ignoring."
                );
                return;
            }

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
                ItemType.RocketLauncher
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

            _activeDroneGo = droneGo;
            _serverSessionActive = true;
            _watchCoroutine = StartCoroutine(WatchDrone(droneGo));

            IssaPluginPlugin.Log.LogInfo(
                $"[HunterDrone] Spawned drone for "
                    + $"{summoner.PlayerId.PlayerName} at {spawnPos:F1}."
            );
        }

        private IEnumerator WatchDrone(GameObject droneGo)
        {
            while (droneGo != null)
                yield return null;

            _activeDroneGo = null;
            _watchCoroutine = null;

            if (_serverSessionActive)
            {
                _serverSessionActive = false;
                IssaPluginPlugin.Log.LogInfo("[HunterDrone] Drone destroyed — session ended.");
            }
        }

        // ── Hole / server cleanup ─────────────────────────────────────────────

        public override void ServerHoleCleanup()
        {
            if (_serverSessionActive)
                ForceServerCleanup();
        }

        public override void ClientHoleCleanup() { }

        private void ForceServerCleanup()
        {
            if (_watchCoroutine != null)
            {
                StopCoroutine(_watchCoroutine);
                _watchCoroutine = null;
            }

            if (_activeDroneGo != null)
            {
                NetworkServer.Destroy(_activeDroneGo);
                _activeDroneGo = null;
            }

            _serverSessionActive = false;
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
