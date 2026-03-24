using System.Collections;
using System.Collections.Generic;
using IssaPlugin.Overlays;
using Mirror;
using UnityEngine;

namespace IssaPlugin.Items
{
    /// <summary>
    /// Attached to every player object via NetworkBridgePatches.
    ///
    /// Flow:
    ///   1. Client equips Position Swap → PositionSwapOverlay opens on use.
    ///   2. Client selects a target → sends PositionSwapRequestMessage to server.
    ///   3. Server validates, consumes item, broadcasts PositionSwapWarningMessage to all.
    ///   4. All clients spawn warning orbs parented to both players' transforms.
    ///   5. After PositionSwapDelay seconds, server swaps both transforms server-side.
    ///   6. Server sends PositionSwapTeleportMessage to each affected client's connection.
    ///   7. Server broadcasts PositionSwapExecuteMessage → smoke VFX, close UI on all clients.
    /// </summary>
    public class PositionSwapNetworkBridge : NetworkBridgeBase
    {
        // ================================================================
        //  Server state (per-instance)
        // ================================================================

        private bool _isServerRoutineActive;
        private Coroutine _serverRoutine;
        private PlayerInventory _inventory;

        private void Awake() => _inventory = GetComponent<PlayerInventory>();

        // ================================================================
        //  Server-side request handler
        //  Called from NetworkManagerPatches when PositionSwapRequestMessage arrives.
        // ================================================================

        public void ServerHandleRequest(uint targetNetId)
        {
            if (_isServerRoutineActive)
            {
                IssaPluginPlugin.Log.LogWarning("[PositionSwap] Routine already active for this player.");
                return;
            }

            if (_inventory == null)
                return;

            var equipped = _inventory.GetEffectivelyEquippedItem(true);
            if (equipped != ItemRegistry.PositionSwapItemType)
            {
                IssaPluginPlugin.Log.LogWarning("[PositionSwap] Item not equipped on initiator.");
                return;
            }

            // Prevent self-swap: item would be consumed with no effect.
            if (targetNetId == netId)
            {
                IssaPluginPlugin.Log.LogWarning("[PositionSwap] Initiator targeted themselves.");
                return;
            }

            if (!NetworkServer.spawned.TryGetValue(targetNetId, out var targetIdentity))
            {
                IssaPluginPlugin.Log.LogWarning($"[PositionSwap] Target netId {targetNetId} not found.");
                // No warning was broadcast yet so only the initiator needs to know.
                connectionToClient?.Send(new PositionSwapCancelledMessage { InitiatorNetId = netId });
                return;
            }

            var targetInventory = targetIdentity.GetComponent<PlayerInventory>();
            if (targetInventory == null)
            {
                IssaPluginPlugin.Log.LogWarning("[PositionSwap] Target has no PlayerInventory.");
                connectionToClient?.Send(new PositionSwapCancelledMessage { InitiatorNetId = netId });
                return;
            }

            // Consume the item before starting the coroutine.
            ItemHelper.ConsumeEquippedItem(_inventory);

            float delay = Configuration.PositionSwapDelay.Value;

            NetworkServer.SendToAll(new PositionSwapWarningMessage
            {
                InitiatorNetId = netId,
                TargetNetId = targetNetId,
                Delay = delay,
            });

            _isServerRoutineActive = true;
            _serverRoutine = StartCoroutine(ServerSwapRoutine(_inventory, targetInventory, delay));
        }

        // ================================================================
        //  Server swap coroutine
        // ================================================================

        private IEnumerator ServerSwapRoutine(
            PlayerInventory initiatorInventory,
            PlayerInventory targetInventory,
            float delay
        )
        {
            yield return new WaitForSeconds(delay);

            // Re-validate — either player may have disconnected during the delay.
            if (
                initiatorInventory == null
                || targetInventory == null
                || initiatorInventory.gameObject == null
                || targetInventory.gameObject == null
            )
            {
                IssaPluginPlugin.Log.LogWarning("[PositionSwap] Player gone before swap could execute.");
                // Broadcast so every client can destroy the warning orbs that were spawned.
                NetworkServer.SendToAll(new PositionSwapCancelledMessage { InitiatorNetId = netId });
                _isServerRoutineActive = false;
                _serverRoutine = null;
                yield break;
            }

            var initiatorInfo = initiatorInventory.PlayerInfo;
            var targetInfo = targetInventory.PlayerInfo;

            Vector3 initiatorOldPos = initiatorInfo.transform.position;
            Vector3 targetOldPos = targetInfo.transform.position;

            // Move server-side transforms so server-authoritative systems see the swap.
            initiatorInfo.transform.position = targetOldPos;
            targetInfo.transform.position = initiatorOldPos;

            // Tell each affected client to warp their own character controller.
            connectionToClient?.Send(new PositionSwapTeleportMessage { NewPosition = targetOldPos });
            targetInventory
                .GetComponent<PositionSwapNetworkBridge>()
                ?.connectionToClient
                ?.Send(new PositionSwapTeleportMessage { NewPosition = initiatorOldPos });

            // Broadcast the execute event so all clients can play VFX and close UI.
            NetworkServer.SendToAll(new PositionSwapExecuteMessage
            {
                InitiatorNetId = netId,
                TargetNetId = targetInventory.netId,
                InitiatorNewPosition = targetOldPos,
                TargetNewPosition = initiatorOldPos,
            });

            IssaPluginPlugin.Log.LogInfo(
                $"[PositionSwap] Swapped positions: initiator→{targetOldPos}, target→{initiatorOldPos}"
            );

            _isServerRoutineActive = false;
            _serverRoutine = null;
        }

        // ================================================================
        //  Client message handlers (static — registered in NetworkManagerPatches)
        // ================================================================

        public static void HandleWarning(PositionSwapWarningMessage msg)
        {
            PositionSwapOverlay.Instance?.OnSwapWarning(msg.InitiatorNetId, msg.TargetNetId, msg.Delay);
            SpawnWarningOrbs(msg.InitiatorNetId, msg.TargetNetId);
        }

        /// Called only on the specific client whose position is changing.
        public static void HandleTeleport(PositionSwapTeleportMessage msg)
        {
            var playerInfo = GameManager.LocalPlayerInfo;
            if (playerInfo == null)
                return;

            // Disable CharacterController before repositioning to avoid being stuck inside colliders.
            var cc = playerInfo.GetComponent<CharacterController>();
            if (cc != null)
                cc.enabled = false;

            playerInfo.transform.position = msg.NewPosition;

            if (cc != null)
                cc.enabled = true;

            // Zero momentum so the player doesn't continue with pre-swap velocity.
            var rb = playerInfo.GetComponent<Rigidbody>();
            if (rb != null)
                rb.linearVelocity = Vector3.zero;

            IssaPluginPlugin.Log.LogInfo($"[PositionSwap] Local player teleported to {msg.NewPosition}");
        }

        public static void HandleExecute(PositionSwapExecuteMessage msg)
        {
            PositionSwapOverlay.Instance?.OnSwapExecuted(msg.InitiatorNetId, msg.TargetNetId);
            DestroyWarningOrbs(msg.InitiatorNetId);
            SpawnSmokeAtPositions(msg.InitiatorNewPosition, msg.TargetNewPosition);
        }

        public static void HandleCancelled(PositionSwapCancelledMessage msg)
        {
            // Clean up orbs on all clients regardless of who initiated.
            DestroyWarningOrbs(msg.InitiatorNetId);
            // Only close the overlay UI on the initiator's client.
            PositionSwapOverlay.Instance?.OnSwapCancelled(msg.InitiatorNetId);
            IssaPluginPlugin.Log.LogInfo("[PositionSwap] Swap cancelled.");
        }

        // ================================================================
        //  VFX helpers — local non-networked objects, one set per pending swap
        // ================================================================

        // Keyed by initiator netId in case multiple swaps are in flight simultaneously.
        private static readonly Dictionary<uint, (GameObject initiatorOrb, GameObject targetOrb)> _pendingOrbs =
            new Dictionary<uint, (GameObject, GameObject)>();

        private static void SpawnWarningOrbs(uint initiatorNetId, uint targetNetId)
        {
            var orbPrefab = AssetLoader.PositionSwapOrbPrefab;
            if (orbPrefab == null)
                return;

            GameObject initiatorOrb = null;
            GameObject targetOrb = null;

            if (NetworkClient.spawned.TryGetValue(initiatorNetId, out var initIdentity))
            {
                initiatorOrb = Object.Instantiate(orbPrefab);
                // Parent to the player so the orb follows them during the delay.
                initiatorOrb.transform.SetParent(initIdentity.transform, false);
                initiatorOrb.transform.localPosition = Vector3.zero;
            }

            if (NetworkClient.spawned.TryGetValue(targetNetId, out var targIdentity))
            {
                targetOrb = Object.Instantiate(orbPrefab);
                targetOrb.transform.SetParent(targIdentity.transform, false);
                targetOrb.transform.localPosition = Vector3.zero;
            }

            _pendingOrbs[initiatorNetId] = (initiatorOrb, targetOrb);
        }

        private static void DestroyWarningOrbs(uint initiatorNetId)
        {
            if (_pendingOrbs.TryGetValue(initiatorNetId, out var pair))
            {
                if (pair.initiatorOrb != null)
                    Object.Destroy(pair.initiatorOrb);
                if (pair.targetOrb != null)
                    Object.Destroy(pair.targetOrb);
                _pendingOrbs.Remove(initiatorNetId);
            }
        }

        private static void SpawnSmokeAtPositions(Vector3 pos1, Vector3 pos2)
        {
            var smokePrefab = AssetLoader.PositionSwapSmokePrefab;
            if (smokePrefab == null)
                return;

            Object.Instantiate(smokePrefab, pos1, Quaternion.identity);
            Object.Instantiate(smokePrefab, pos2, Quaternion.identity);
        }

        // ================================================================
        //  Hole-transition cleanup
        // ================================================================

        public override void ServerHoleCleanup()
        {
            if (!_isServerRoutineActive)
                return;

            if (_serverRoutine != null)
            {
                StopCoroutine(_serverRoutine);
                _serverRoutine = null;
            }

            NetworkServer.SendToAll(new PositionSwapCancelledMessage { InitiatorNetId = netId });
            _isServerRoutineActive = false;
            IssaPluginPlugin.Log.LogInfo("[PositionSwap] Server state cleared on hole transition.");
        }

        public override void OnStopServer()
        {
            if (!_isServerRoutineActive)
                return;

            if (_serverRoutine != null)
            {
                StopCoroutine(_serverRoutine);
                _serverRoutine = null;
            }

            _isServerRoutineActive = false;
            IssaPluginPlugin.Log.LogInfo("[PositionSwap] Server state cleared on server stop.");
            // Don't SendToAll here — the server is stopping and the transport is shutting down.
        }

        public override void ClientHoleCleanup()
        {
            foreach (var pair in _pendingOrbs)
            {
                if (pair.Value.initiatorOrb != null)
                    Object.Destroy(pair.Value.initiatorOrb);
                if (pair.Value.targetOrb != null)
                    Object.Destroy(pair.Value.targetOrb);
            }
            _pendingOrbs.Clear();

            // Force-close the overlay for the local player regardless of who initiated.
            var overlay = PositionSwapOverlay.Instance;
            if (overlay != null)
                overlay.ForceClose();
        }
    }
}
