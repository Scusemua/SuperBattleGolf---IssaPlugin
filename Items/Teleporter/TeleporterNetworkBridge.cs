using IssaPlugin.Network;
using Mirror;
using UnityEngine;

namespace IssaPlugin.Items
{
    /// <summary>
    /// Attached to every player object via <see cref="AddBridgeComponentsPatch"/>.
    ///
    /// Network flow:
    ///   1. Local client confirms destination → sends <see cref="TeleporterUseMessage"/>.
    ///   2. Server receives it here in <see cref="ServerHandleUse"/> →
    ///      consumes item, records origin, moves Rigidbody server-side.
    ///   3. Server sends <see cref="TeleporterWarpMessage"/> to the using client's
    ///      connection so it warps the local CharacterController.
    ///   4. Server broadcasts <see cref="TeleporterVfxMessage"/> → all clients
    ///      instantiate the teleport particle effect at origin and destination.
    ///
    /// The [ClientRpc] weaver is not run on plugin DLLs, so all server→client
    /// messages use NetworkServer.SendToAll / connectionToClient.Send with
    /// manual NetworkMessage structs — identical to every other bridge in this mod.
    /// </summary>
    public class TeleporterNetworkBridge : NetworkBridgeBase
    {
        private bool _isActive;

        // Cached so OnDestroy can check ownership after Mirror tears down network state.
        private bool _wasOwned;

        // ================================================================
        //  Server-side request handler
        //  Called from NetworkManagerPatches when TeleporterUseMessage arrives.
        // ================================================================

        public void ServerHandleUse(Vector3 destination, int equippedIndex)
        {
            if (_isActive)
            {
                IssaPluginPlugin.Log.LogWarning(
                    "[Teleporter] Teleport already in progress for this player."
                );
                return;
            }

            var inv = GetComponent<PlayerInventory>();
            if (inv == null)
                return;

            _isActive = true;

            // Broadcast an item-warning banner to all clients (same as StealthBomber).
            ItemWarningBroadcaster.Broadcast(
                inv.PlayerInfo.PlayerId.PlayerName,
                ItemRegistry.TeleporterItemType,
                "Teleporter",
                senderNetId: netId
            );

            // Record origin before moving anything.
            var playerInfo = inv.PlayerInfo;
            Vector3 origin = playerInfo.Rigidbody.position;

            IssaPluginPlugin.Log.LogInfo(
                $"[Teleporter] Player '{playerInfo.PlayerId.PlayerName}' teleporting "
                    + $"from {origin} to {destination}."
            );

            // ── Consume item (server-authoritative) ──────────────────────────
            ItemHelper.SetCurrentItemUse(inv, ItemUseType.Regular);
            if (equippedIndex >= 0)
                ItemHelper.DecrementAndRemove(inv, equippedIndex);
            ItemHelper.SetCurrentItemUse(inv, ItemUseType.None);

            // ── Move Rigidbody server-side ────────────────────────────────────
            // Mirror's NetworkTransform will sync the new position to all clients.
            playerInfo.Rigidbody.position = destination;
            playerInfo.Rigidbody.linearVelocity = Vector3.zero;
            playerInfo.Rigidbody.angularVelocity = Vector3.zero;

            // ── Tell the using client to warp its CharacterController ─────────
            // This mirrors exactly how PositionSwapNetworkBridge handles warping:
            // the host's local client calls the handler directly; remote clients
            // receive it via their connection.
            if (isLocalPlayer)
                TeleporterNetworkBridge.HandleWarp(
                    new TeleporterWarpMessage { Destination = destination }
                );
            else if (connectionToClient != null)
                connectionToClient.Send(new TeleporterWarpMessage { Destination = destination });

            // ── Broadcast VFX to all clients ─────────────────────────────────
            NetworkServer.SendToAll(
                new TeleporterVfxMessage { Origin = origin, Destination = destination }
            );

            _isActive = false;
        }

        // ================================================================
        //  Client message handlers (static — registered in NetworkManagerPatches)
        // ================================================================

        /// <summary>
        /// Received only by the using client.
        /// Uses the game's own Movement.Teleport() exactly as PositionSwapNetworkBridge does
        /// to correctly zero velocity and sync the warp via CmdTeleport.
        /// </summary>
        public static void HandleWarp(TeleporterWarpMessage msg)
        {
            var playerInfo = GameManager.LocalPlayerInfo;
            if (playerInfo == null)
                return;

            playerInfo.Movement.Teleport(
                msg.Destination,
                playerInfo.Movement.transform.rotation,
                false
            );

            IssaPluginPlugin.Log.LogInfo($"[Teleporter] Local player warped to {msg.Destination}.");
        }

        /// <summary>
        /// Received by all clients.
        /// Spawns the teleport VFX particle system at both the origin and the destination.
        /// </summary>
        public static void HandleVfx(TeleporterVfxMessage msg)
        {
            SpawnTeleportVfx(msg.Origin);
            SpawnTeleportVfx(msg.Destination);
        }

        private static void SpawnTeleportVfx(Vector3 position)
        {
            var prefab = AssetLoader.TeleporterVfxPrefab;
            if (prefab == null)
            {
                IssaPluginPlugin.Log.LogWarning(
                    "[Teleporter] VFX prefab not loaded — skipping effect."
                );
                return;
            }

            var go = Object.Instantiate(prefab, position, Quaternion.identity);

            // Auto-destroy once the particle system has finished playing.
            // If the prefab loops, fall back to a fixed lifetime.
            var ps = go.GetComponent<ParticleSystem>();
            if (ps != null && !ps.main.loop)
            {
                Object.Destroy(go, ps.main.duration + ps.main.startLifetime.constantMax);
            }
            else
            {
                Object.Destroy(go, 5f);
            }
        }

        // ================================================================
        //  Lifecycle
        // ================================================================

        public override void OnStartClient()
        {
            _wasOwned = isOwned;
        }

        public override void OnStopClient()
        {
            if (isOwned)
                TeleporterItem.CancelTargeting();
        }

        private void OnDestroy()
        {
            if (_wasOwned)
                TeleporterItem.CancelTargeting();
        }

        public override void ServerHoleCleanup() { }

        public override void ClientHoleCleanup() { }
    }
}
