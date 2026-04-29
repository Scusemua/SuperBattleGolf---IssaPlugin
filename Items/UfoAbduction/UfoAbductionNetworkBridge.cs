using IssaPlugin.Overlays;
using Mirror;
using UnityEngine;

namespace IssaPlugin.Items
{
    /// <summary>
    /// Attached to every player object via NetworkBridgePatches.
    ///
    /// Handles the UFO Abduction item's lock-on / single-victim session lifecycle.
    ///
    /// Message flow:
    ///   Client → Server : UfoAbductionLockOnMessage
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
        private NetworkConnectionToClient _wielderConn;
        private PlayerInfo _targetInfo;
        private Coroutine _serverCoroutine;
        private int _wielderSlot = -1;
        private float _serverSessionStartTime;

        // Positions computed once at session start and reused by ServerHoleCleanup.
        private Vector3 _serverHoverPos;
        private Vector3 _serverExplosionPos;
        private float _serverApproachDuration;
        private float _serverAbductionDuration;
        private float _serverAscentDuration;

        // ── Unity lifecycle ───────────────────────────────────────────────────

        private void Update()
        {
            if (!isOwned)
                return;

            UfoAbductionClientLogic.UpdateAll();
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

            if (targetIdentity == GetComponent<NetworkIdentity>())
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

            // 5. Compute positions from config
            Vector3 victimPos = targetInfo.transform.position;
            float hoverHeight = ModConfig.UfoAbduction.HoverHeight.Value;
            float ascentExtra = ModConfig.UfoAbduction.AscentExtraHeight.Value;

            Vector3 hoverPos = victimPos + Vector3.up * hoverHeight;
            Vector3 explosionPos = hoverPos + Vector3.up * ascentExtra;

            // UFO spawns high above and to a random horizontal side of the victim
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
            float ascentDuration = ModConfig.UfoAbduction.AscentDuration.Value;

            // 6. Store server session state
            _serverSessionActive = true;
            _wielderConn = conn;
            _targetInfo = targetInfo;
            _serverSessionStartTime = Time.time;
            _serverHoverPos = hoverPos;
            _serverExplosionPos = explosionPos;
            _serverApproachDuration = approachDuration;
            _serverAbductionDuration = abductionDuration;
            _serverAscentDuration = ascentDuration;

            IssaPluginPlugin.Log.LogInfo(
                $"[UfoAbduction] Server: session started. wielder={wielderNetId} target={targetIdentity.netId}"
            );

            // 7. Broadcast begin to all clients

            NetworkServer.SendToAll(
                new UfoAbductionBeginMessage
                {
                    WielderNetId = wielderNetId,
                    VictimNetId = targetIdentity.netId,
                    UfoSpawnPos = ufoSpawnPos,
                    HoverPos = hoverPos,
                    ExplosionPos = explosionPos,
                    ApproachDuration = approachDuration,
                    AbductionDuration = abductionDuration,
                    AscentDuration = ascentDuration,
                    SpringForce = ModConfig.UfoAbduction.SpringForce.Value,
                    MaxPullSpeed = ModConfig.UfoAbduction.MaxPullSpeed.Value,
                    NaturalLength = ModConfig.UfoAbduction.NaturalLength.Value,
                    ExplosionForce = ModConfig.UfoAbduction.ExplosionForce.Value,
                    ExplosionRadius = ModConfig.UfoAbduction.ExplosionRadius.Value,
                }
            );

            // 8. Start server timeout coroutine
            float totalDuration = approachDuration + abductionDuration + ascentDuration;
            _serverCoroutine = StartCoroutine(ServerSessionCoroutine(totalDuration, explosionPos));
        }

        // ── Server — session coroutine ────────────────────────────────────────

        private System.Collections.IEnumerator ServerSessionCoroutine(
            float duration,
            Vector3 explosionPos
        )
        {
            yield return new WaitForSeconds(duration);

            if (!_serverSessionActive)
                yield break;

            IssaPluginPlugin.Log.LogInfo("[UfoAbduction] Server: UFO exploding.");
            ServerEndSession(
                explosionPos,
                ModConfig.UfoAbduction.ExplosionForce.Value,
                ModConfig.UfoAbduction.ExplosionRadius.Value
            );
        }

        // ── Server — session lifecycle ────────────────────────────────────────

        private void ServerEndSession(
            Vector3 explosionPos,
            float explosionForce,
            float explosionRadius
        )
        {
            if (!_serverSessionActive)
                return;

            _serverSessionActive = false;

            // Apply explosion to non-player Rigidbodies server-side
            var colliders = Physics.OverlapSphere(explosionPos, explosionRadius);
            foreach (var col in colliders)
            {
                var rb = col.attachedRigidbody;
                if (rb == null || rb.isKinematic)
                    continue;
                if (col.GetComponentInParent<PlayerInfo>() != null)
                    continue;
                rb.AddExplosionForce(
                    explosionForce,
                    explosionPos,
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

            // Broadcast end to all clients
            NetworkServer.SendToAll(
                new UfoAbductionEndMessage
                {
                    VictimNetId = _targetInfo.GetComponent<NetworkIdentity>().netId,
                    ExplosionPos = explosionPos,
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

        // ── Hole cleanup ──────────────────────────────────────────────────────

        public override void ServerHoleCleanup()
        {
            if (_serverSessionActive)
            {
                // Use the stored positions from session start so the explosion position
                // matches what was already broadcast to clients in UfoAbductionBeginMessage.
                float elapsed = Time.time - _serverSessionStartTime;
                float ascentElapsed = elapsed - _serverApproachDuration - _serverAbductionDuration;
                Vector3 explosionPos;
                if (ascentElapsed > 0f && _serverAscentDuration > 0f)
                {
                    float t = Mathf.Clamp01(ascentElapsed / _serverAscentDuration);
                    explosionPos = Vector3.Lerp(_serverHoverPos, _serverExplosionPos, t);
                }
                else
                {
                    explosionPos = _serverHoverPos;
                }

                ServerEndSession(
                    explosionPos,
                    ModConfig.UfoAbduction.ExplosionForce.Value,
                    ModConfig.UfoAbduction.ExplosionRadius.Value
                );
            }

            if (_serverCoroutine != null)
            {
                StopCoroutine(_serverCoroutine);
                _serverCoroutine = null;
            }
        }

        public override void ClientHoleCleanup()
        {
            UfoAbductionClientLogic.ClearAll();
        }
    }
}
