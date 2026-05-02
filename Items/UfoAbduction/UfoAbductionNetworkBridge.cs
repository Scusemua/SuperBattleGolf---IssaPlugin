using IssaPlugin.Overlays;
using Mirror;
using UnityEngine;

namespace IssaPlugin.Items
{
    /// <summary>
    /// Attached to every player object via NetworkBridgePatches.
    ///
    /// Handles the UFO Abduction item's two-phase server flow:
    ///   Phase A (pre-begin): target locked, wielder selects drop-off zone via targeting UI.
    ///   Phase B (session):   UFO animates, victim is transported to chosen destination.
    ///
    /// Message flow:
    ///   Client → Server : UfoAbductionLockOnMessage
    ///   Server → Wielder: UfoAbductionTargetAcquiredMessage
    ///   Server → Victim : UfoAbductionBeingTargetedMessage
    ///   Client → Server : UfoAbductionDropoffSelectedMessage  (wielder confirms)
    ///   Client → Server : UfoAbductionDropoffCancelledMessage (wielder cancels)
    ///   Server → All    : UfoAbductionSessionAbortedMessage   (any pre-begin cancel)
    ///   Server → All    : UfoAbductionBeginMessage
    ///   Server → All    : UfoAbductionEndMessage
    ///   Server → Wielder: UfoAbductionBusyMessage
    ///
    /// Global one-at-a-time lock via GlobalSessionLock&lt;UfoAbductionNetworkBridge&gt;.
    /// </summary>
    public class UfoAbductionNetworkBridge : NetworkBridgeBase
    {
        // ── Server-side session state (wielder's bridge only) ─────────────────

        private bool _serverSessionActive;
        private bool _serverWaitingForDropoff;
        private NetworkConnectionToClient _wielderConn;
        private PlayerInfo _targetInfo;
        private Coroutine _serverCoroutine;
        private Coroutine _serverDropoffTimeoutCoroutine;
        private int _wielderSlot = -1;

        // Ground-level destination; stored for ServerEndSession explosion.
        private Vector3 _serverExplosionPos;

        // Cached for OnDestroy check after Mirror tears down network state.
        private bool _wasOwned;

        // ── Unity lifecycle ───────────────────────────────────────────────────

        private void Update()
        {
            if (!isOwned)
                return;

            UfoAbductionClientLogic.UpdateAll();
        }

        public override void OnStartClient()
        {
            _wasOwned = isOwned;
        }

        public override void OnStopClient()
        {
            if (isOwned)
                UfoAbductionTargeting.CancelSelection();
        }

        private void OnDestroy()
        {
            if (_wasOwned)
                UfoAbductionTargeting.CancelSelection();
        }

        // ── Client — initiating the lock-on ──────────────────────────────────

        public void ClientUse()
        {
            if (!isOwned)
                return;

            var bestTarget = UfoAbductionOverlay.Instance?.BestTargetIdentity;
            if (bestTarget == null)
            {
                IssaPluginPlugin.Log.LogInfo("[UfoAbduction] ClientUse: no valid target in cone.");
                return;
            }

            IssaPluginPlugin.Log.LogInfo(
                $"[UfoAbduction] ClientUse: targeting netId={bestTarget.netId}."
            );
            NetworkClient.Send(new UfoAbductionLockOnMessage { TargetNetId = bestTarget.netId });
        }

        // ── Server — handling lock-on request ────────────────────────────────

        public void ServerHandleLockOn(
            NetworkConnectionToClient conn,
            UfoAbductionLockOnMessage msg
        )
        {
            if (!isServer)
                return;

            // 1. Acquire global session lock
            if (!GlobalSessionLock<UfoAbductionNetworkBridge>.TryAcquire(this))
            {
                IssaPluginPlugin.Log.LogInfo(
                    "[UfoAbduction] Server: session busy — rejecting lock-on."
                );
                conn.Send(new UfoAbductionBusyMessage());
                return;
            }

            // 2. Validate wielder has the item
            var inventory = GetComponent<PlayerInventory>();
            int slot =
                inventory != null
                    ? ItemRegistry.FindSlotIndex(inventory, ItemRegistry.UfoAbductionItemType)
                    : -1;
            if (slot < 0)
            {
                IssaPluginPlugin.Log.LogWarning(
                    "[UfoAbduction] Server: wielder does not have UFO Abduction."
                );
                GlobalSessionLock<UfoAbductionNetworkBridge>.Release();
                return;
            }
            _wielderSlot = slot;

            // 3. Validate target
            if (!NetworkServer.spawned.TryGetValue(msg.TargetNetId, out var targetIdentity))
            {
                IssaPluginPlugin.Log.LogWarning(
                    $"[UfoAbduction] Server: target netId={msg.TargetNetId} not found."
                );
                GlobalSessionLock<UfoAbductionNetworkBridge>.Release();
                _wielderSlot = -1;
                return;
            }

            var targetInfo = targetIdentity.GetComponentInParent<PlayerInfo>();
            if (targetInfo == null)
            {
                IssaPluginPlugin.Log.LogWarning("[UfoAbduction] Server: target is not a player.");
                GlobalSessionLock<UfoAbductionNetworkBridge>.Release();
                _wielderSlot = -1;
                return;
            }

            bool isSoloPlay = NetworkServer.connections.Count <= 1;
            if (!isSoloPlay && targetIdentity == GetComponent<NetworkIdentity>())
            {
                IssaPluginPlugin.Log.LogWarning("[UfoAbduction] Server: self-targeting rejected.");
                GlobalSessionLock<UfoAbductionNetworkBridge>.Release();
                _wielderSlot = -1;
                return;
            }

            // 4. Electromagnetic shield check
            if (targetInfo.IsElectromagnetShieldActive)
            {
                IssaPluginPlugin.Log.LogInfo(
                    "[UfoAbduction] Server: target has electromagnetic shield — rejecting."
                );
                GlobalSessionLock<UfoAbductionNetworkBridge>.Release();
                _wielderSlot = -1;
                targetInfo.PlayElectromagnetShieldHitForAllClients(
                    (targetInfo.transform.position - transform.position).normalized
                );
                return;
            }

            // 5. Enter waiting-for-dropoff state
            _serverWaitingForDropoff = true;
            _wielderConn = conn;
            _targetInfo = targetInfo;

            _serverDropoffTimeoutCoroutine = StartCoroutine(ServerDropoffTimeoutCoroutine());

            IssaPluginPlugin.Log.LogInfo(
                $"[UfoAbduction] Server: target acquired. wielder={GetComponent<NetworkIdentity>().netId} victim={targetIdentity.netId}"
            );

            // Tell wielder to open targeting UI
            conn.Send(
                new UfoAbductionTargetAcquiredMessage { VictimPos = targetInfo.transform.position }
            );

            // Notify victim they are being targeted
            if (targetIdentity.isLocalPlayer)
                HandleBeingTargeted(new UfoAbductionBeingTargetedMessage());
            else
                targetIdentity.connectionToClient?.Send(new UfoAbductionBeingTargetedMessage());
        }

        // ── Server — drop-off selected ────────────────────────────────────────

        public void ServerHandleDropoffSelected(
            NetworkConnectionToClient conn,
            UfoAbductionDropoffSelectedMessage msg
        )
        {
            if (!isServer)
                return;

            if (!_serverWaitingForDropoff || conn != _wielderConn)
                return;

            // Re-validate victim is still connected
            var targetNetId = _targetInfo.GetComponent<NetworkIdentity>().netId;
            if (!NetworkServer.spawned.ContainsKey(targetNetId))
            {
                IssaPluginPlugin.Log.LogWarning(
                    "[UfoAbduction] Server: victim disconnected before drop-off selected — aborting."
                );
                ServerAbortWaiting();
                return;
            }

            StopCoroutine(_serverDropoffTimeoutCoroutine);
            _serverDropoffTimeoutCoroutine = null;
            _serverWaitingForDropoff = false;
            _serverSessionActive = true;
            _serverExplosionPos = msg.Destination;

            Vector3 victimPos = _targetInfo.transform.position;
            float hoverHeight = ModConfig.UfoAbduction.HoverHeight.Value;
            Vector3 hoverPos = victimPos + Vector3.up * hoverHeight;
            Vector3 dropoffPos =
                msg.Destination + Vector3.up * ModConfig.UfoAbduction.DropHeight.Value;

            float spawnAngle = UnityEngine.Random.Range(0f, 360f) * Mathf.Deg2Rad;
            Vector3 ufoSpawnPos =
                victimPos
                + new Vector3(
                    Mathf.Cos(spawnAngle) * 40f,
                    hoverHeight + 30f,
                    Mathf.Sin(spawnAngle) * 40f
                );

            uint wielderNetId = GetComponent<NetworkIdentity>().netId;

            float approachDuration = ModConfig.UfoAbduction.ApproachDuration.Value;
            float abductionDuration = ModConfig.UfoAbduction.AbductionDuration.Value;
            float transitDuration = ModConfig.UfoAbduction.AscentDuration.Value;

            IssaPluginPlugin.Log.LogInfo(
                $"[UfoAbduction] Server: session started. wielder={wielderNetId} victim={targetNetId} dropoff={msg.Destination}"
            );

            NetworkServer.SendToAll(
                new UfoAbductionBeginMessage
                {
                    WielderNetId = wielderNetId,
                    VictimNetId = targetNetId,
                    UfoSpawnPos = ufoSpawnPos,
                    HoverPos = hoverPos,
                    DropoffPos = dropoffPos,
                    ApproachDuration = approachDuration,
                    AbductionDuration = abductionDuration,
                    TransitDuration = transitDuration,
                    MaxPullSpeed = ModConfig.UfoAbduction.MaxPullSpeed.Value,
                    NaturalLength = ModConfig.UfoAbduction.NaturalLength.Value,
                    ExplosionForce = ModConfig.UfoAbduction.ExplosionForce.Value,
                    ExplosionRadius = ModConfig.UfoAbduction.ExplosionRadius.Value,
                    HoverHeight = hoverHeight,
                }
            );

            float totalDuration = approachDuration + abductionDuration + transitDuration;
            _serverCoroutine = StartCoroutine(ServerSessionCoroutine(totalDuration));
        }

        // ── Server — drop-off cancelled ───────────────────────────────────────

        public void ServerHandleDropoffCancelled(
            NetworkConnectionToClient conn,
            UfoAbductionDropoffCancelledMessage msg
        )
        {
            if (!isServer)
                return;

            if (!_serverWaitingForDropoff || conn != _wielderConn)
                return;

            IssaPluginPlugin.Log.LogInfo(
                "[UfoAbduction] Server: wielder cancelled drop-off selection."
            );
            ServerAbortWaiting();
        }

        // ── Server — centralized pre-begin abort ──────────────────────────────

        private void ServerAbortWaiting()
        {
            _serverWaitingForDropoff = false;

            if (_serverDropoffTimeoutCoroutine != null)
            {
                StopCoroutine(_serverDropoffTimeoutCoroutine);
                _serverDropoffTimeoutCoroutine = null;
            }

            // Notify all clients: wielder's targeting UI exits, victim's banner clears
            NetworkServer.SendToAll(new UfoAbductionSessionAbortedMessage());

            GlobalSessionLock<UfoAbductionNetworkBridge>.Release();

            _wielderConn = null;
            _targetInfo = null;
            _wielderSlot = -1;
        }

        // ── Server — drop-off selection timeout ───────────────────────────────

        private System.Collections.IEnumerator ServerDropoffTimeoutCoroutine()
        {
            yield return new WaitForSeconds(ModConfig.UfoAbduction.DropoffSelectionTimeout.Value);

            if (_serverWaitingForDropoff)
            {
                IssaPluginPlugin.Log.LogInfo(
                    "[UfoAbduction] Server: drop-off selection timed out."
                );
                ServerAbortWaiting();
            }
        }

        // ── Server — session coroutine ────────────────────────────────────────

        private System.Collections.IEnumerator ServerSessionCoroutine(float duration)
        {
            yield return new WaitForSeconds(duration);

            if (!_serverSessionActive)
                yield break;

            IssaPluginPlugin.Log.LogInfo(
                "[UfoAbduction] Server: session ended — applying explosion."
            );
            ServerEndSession();
        }

        // ── Server — session lifecycle ────────────────────────────────────────

        private void ServerEndSession()
        {
            if (!_serverSessionActive)
                return;

            _serverSessionActive = false;

            // Apply explosion to non-player Rigidbodies at the destination
            float explosionForce = ModConfig.UfoAbduction.ExplosionForce.Value;
            float explosionRadius = ModConfig.UfoAbduction.ExplosionRadius.Value;

            var colliders = Physics.OverlapSphere(_serverExplosionPos, explosionRadius);
            foreach (var col in colliders)
            {
                var rb = col.attachedRigidbody;
                if (rb == null || rb.isKinematic)
                    continue;
                if (col.GetComponentInParent<PlayerInfo>() != null)
                    continue;
                rb.AddExplosionForce(
                    explosionForce,
                    _serverExplosionPos,
                    explosionRadius,
                    0.5f,
                    ForceMode.VelocityChange
                );
            }

            // Consume item
            if (_wielderSlot >= 0)
            {
                var inventory = GetComponent<PlayerInventory>();
                if (inventory != null)
                {
                    ItemHelper.SetCurrentItemUse(inventory, ItemUseType.Regular);
                    ItemHelper.DecrementAndRemove(inventory, _wielderSlot);
                    ItemHelper.SetCurrentItemUse(inventory, ItemUseType.None);
                }
            }
            _wielderSlot = -1;

            NetworkServer.SendToAll(
                new UfoAbductionEndMessage
                {
                    VictimNetId = _targetInfo.GetComponent<NetworkIdentity>().netId,
                    ExplosionPos = _serverExplosionPos,
                    ExplosionForce = explosionForce,
                    ExplosionRadius = explosionRadius,
                }
            );

            GlobalSessionLock<UfoAbductionNetworkBridge>.Release();

            if (_serverCoroutine != null)
            {
                StopCoroutine(_serverCoroutine);
                _serverCoroutine = null;
            }

            _wielderConn = null;
            _targetInfo = null;
        }

        // ── Client — message handlers ─────────────────────────────────────────

        public static void HandleBusy(UfoAbductionBusyMessage msg)
        {
            IssaPluginPlugin.Log.LogInfo(
                "[UfoAbduction] Session busy — another UFO abduction is already active."
            );
            UfoAbductionOverlay.Instance?.ShowBusy();
        }

        public static void HandleTargetAcquired(UfoAbductionTargetAcquiredMessage msg)
        {
            var movement = GameManager.LocalPlayerInfo?.Movement;
            if (movement == null)
                return;
            movement.StartCoroutine(
                UfoAbductionTargeting.BeginDropoffSelectionRoutine(msg.VictimPos)
            );
        }

        public static void HandleBeingTargeted(UfoAbductionBeingTargetedMessage msg)
        {
            UfoAbductionOverlay.Instance?.ShowBeingTargeted();
        }

        public static void HandleSessionAborted(UfoAbductionSessionAbortedMessage msg)
        {
            // Capture before CancelSelection sets the flag (coroutine hasn't exited yet)
            bool wasSelecting = UfoAbductionTargeting.IsSelectingDropoff;
            UfoAbductionTargeting.CancelSelection();
            UfoAbductionOverlay.Instance?.ClearBeingTargeted();
            if (wasSelecting)
                UfoAbductionOverlay.Instance?.ShowAborted();
        }

        // ── Hole cleanup ──────────────────────────────────────────────────────

        public override void ServerHoleCleanup()
        {
            if (_serverWaitingForDropoff)
                ServerAbortWaiting();

            if (_serverSessionActive)
                ServerEndSession();

            if (_serverCoroutine != null)
            {
                StopCoroutine(_serverCoroutine);
                _serverCoroutine = null;
            }
        }

        public override void ClientHoleCleanup()
        {
            UfoAbductionClientLogic.ClearAll();
            UfoAbductionTargeting.CancelSelection();
        }
    }
}
