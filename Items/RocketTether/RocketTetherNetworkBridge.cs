using IssaPlugin.Overlays;
using Mirror;
using UnityEngine;

namespace IssaPlugin.Items
{
    /// <summary>
    /// Attached to every player object via NetworkBridgePatches.
    ///
    /// Handles the Rocket Tether item's lock-on / single-victim session lifecycle.
    /// All per-victim client state (spring physics, VFX, explosions) has been
    /// extracted into RocketTetherClientLogic so it can be shared with the
    /// Rocket Tether Grenade without duplication.
    ///
    /// Message flow:
    ///   Client → Server : RocketTetherLockOnMessage
    ///   Server → All    : RocketVictimConnectedMessage   (shared with grenade)
    ///   Server → All    : RocketVictimDisconnectedMessage (shared with grenade)
    ///   Server → Wielder: RocketTetherBusyMessage
    ///
    /// Global one-at-a-time lock via GlobalSessionLock&lt;RocketTetherNetworkBridge&gt;.
    /// All server session state lives on the wielder's bridge instance only.
    /// </summary>
    public class RocketTetherNetworkBridge : NetworkBridgeBase
    {
        // =====================================================================
        //  Server-side per-instance state (wielder's bridge only)
        // =====================================================================

        private bool _serverSessionActive;
        private NetworkConnectionToClient _wielderConn;
        private PlayerInfo _targetInfo;
        private Coroutine _serverTimeout;
        private int _wielderLinkerSlot = -1;

        private Vector3 _serverRocketStartPos;
        private float   _serverRocketSpeed;

        // Recorded server-side when the session begins.
        // Used by ServerHoleCleanup to compute an accurate elapsed time without
        // relying on the client-side s_rocketStartTime that was previously here.
        private float _serverSessionStartTime;

        // =====================================================================
        //  Unity lifecycle
        // =====================================================================

        private void Update()
        {
            // Only the local player's bridge drives per-frame visual updates.
            // UpdateAll is a no-op when the shared session dictionary is empty.
            if (!isOwned)
                return;

            RocketTetherClientLogic.UpdateAll();
        }

        // =====================================================================
        //  Client — initiating the lock-on
        // =====================================================================

        /// Called from RocketTetherItemDefinition.OnUse → GetComponent<RocketTetherNetworkBridge>().
        public void ClientUse()
        {
            if (!isOwned)
                return;

            var bestTarget = RocketTetherOverlay.Instance?.BestTargetIdentity;
            if (bestTarget == null)
            {
                IssaPluginPlugin.Log.LogInfo("[RocketTether] ClientUse: no valid target in cone.");
                return;
            }

            IssaPluginPlugin.Log.LogInfo(
                $"[RocketTether] ClientUse: targeting netId={bestTarget.netId}."
            );
            NetworkClient.Send(new RocketTetherLockOnMessage { TargetNetId = bestTarget.netId });
        }

        // =====================================================================
        //  Server — handling lock-on request
        // =====================================================================

        public void ServerHandleLockOn(
            NetworkConnectionToClient conn,
            RocketTetherLockOnMessage msg
        )
        {
            if (!isServer)
                return;

            // ── 1. Acquire global session lock ────────────────────────────────
            if (!GlobalSessionLock<RocketTetherNetworkBridge>.TryAcquire(this))
            {
                IssaPluginPlugin.Log.LogInfo(
                    "[RocketTether] Server: session busy — rejecting lock-on."
                );
                conn.Send(new RocketTetherBusyMessage());
                return;
            }

            // ── 2. Validate wielder has Rocket Tether and find which slot ─────
            var inventory = GetComponent<PlayerInventory>();
            int rocketTetherSlot =
                inventory != null
                    ? ItemRegistry.FindSlotIndex(inventory, ItemRegistry.RocketTetherItemType)
                    : -1;
            if (rocketTetherSlot < 0)
            {
                IssaPluginPlugin.Log.LogWarning(
                    "[RocketTether] Server: wielder does not have Rocket Tether."
                );
                GlobalSessionLock<RocketTetherNetworkBridge>.Release();
                return;
            }

            // ── 3. Cache slot index before any state can change it ────────────
            _wielderLinkerSlot = rocketTetherSlot;

            // ── 4. Validate target — players only, no self-link ───────────────
            if (!NetworkServer.spawned.TryGetValue(msg.TargetNetId, out var targetIdentity))
            {
                IssaPluginPlugin.Log.LogWarning(
                    $"[RocketTether] Server: target netId={msg.TargetNetId} not found."
                );
                GlobalSessionLock<RocketTetherNetworkBridge>.Release();
                _wielderLinkerSlot = -1;
                return;
            }

            var targetInfo = targetIdentity.GetComponentInParent<PlayerInfo>();
            if (targetInfo == null)
            {
                IssaPluginPlugin.Log.LogWarning("[RocketTether] Server: target is not a player.");
                GlobalSessionLock<RocketTetherNetworkBridge>.Release();
                _wielderLinkerSlot = -1;
                return;
            }

            if (targetIdentity == GetComponent<NetworkIdentity>())
            {
                IssaPluginPlugin.Log.LogWarning("[RocketTether] Server: self-targeting rejected.");
                GlobalSessionLock<RocketTetherNetworkBridge>.Release();
                _wielderLinkerSlot = -1;
                return;
            }

            // ── 4b. Electromagnetic shield check ─────────────────────────────
            if (targetInfo.IsElectromagnetShieldActive)
            {
                IssaPluginPlugin.Log.LogInfo(
                    "[RocketTether] Server: target has electromagnetic shield — rejecting."
                );
                GlobalSessionLock<RocketTetherNetworkBridge>.Release();
                _wielderLinkerSlot = -1;
                targetInfo.PlayElectromagnetShieldHitForAllClients(
                    (targetInfo.transform.position - transform.position).normalized
                );
                return;
            }

            var wielderNetId = GetComponent<NetworkIdentity>().netId;

            // ── 5. Compute rocket spawn position ──────────────────────────────
            float rocketSpeed = ModConfig.RocketTether.RocketSpeed.Value;
            float duration    = ModConfig.RocketTether.TetherDuration.Value;
            Vector3 rocketStart = targetInfo.transform.position + Vector3.up * 1.5f;

            // ── 6. Store server session state ─────────────────────────────────
            _serverSessionActive    = true;
            _wielderConn            = conn;
            _targetInfo             = targetInfo;
            _serverRocketStartPos   = rocketStart;
            _serverRocketSpeed      = rocketSpeed;
            _serverSessionStartTime = Time.time;

            IssaPluginPlugin.Log.LogInfo(
                $"[RocketTether] Server: session started. wielder={wielderNetId} target={targetIdentity.netId}"
            );

            // ── 7. Broadcast to all clients using the shared message ──────────
            NetworkServer.SendToAll(
                new RocketVictimConnectedMessage
                {
                    WielderNetId   = wielderNetId,
                    VictimNetId    = targetIdentity.netId,
                    RocketStartPos = rocketStart,
                    RocketSpeed    = rocketSpeed,
                    Duration       = duration,
                    SpringForce    = ModConfig.RocketTether.SpringForce.Value,
                    MaxPullSpeed   = ModConfig.RocketTether.MaxPullSpeed.Value,
                    NaturalLength  = ModConfig.RocketTether.NaturalLength.Value,
                }
            );

            // ── 8. Start server timeout / explosion coroutine ─────────────────
            _serverTimeout = StartCoroutine(ServerRocketCoroutine(duration));
        }

        // =====================================================================
        //  Server — rocket flight and explosion
        // =====================================================================

        private System.Collections.IEnumerator ServerRocketCoroutine(float duration)
        {
            yield return new WaitForSeconds(duration);

            if (!_serverSessionActive)
                yield break;

            IssaPluginPlugin.Log.LogInfo("[RocketTether] Server: rocket exploding.");

            Vector3 explosionPos =
                _serverRocketStartPos + Vector3.up * _serverRocketSpeed * duration;

            float explosionForce  = ModConfig.RocketTether.ExplosionForce.Value;
            float explosionRadius = ModConfig.RocketTether.ExplosionRadius.Value;

            // Apply explosion to non-player Rigidbodies.
            // Player physics are client-authoritative; clients handle their own
            // forces via RocketVictimDisconnectedMessage.
            var colliders = Physics.OverlapSphere(explosionPos, explosionRadius);
            foreach (var col in colliders)
            {
                var rb = col.attachedRigidbody;
                if (rb == null || rb.isKinematic) continue;
                if (col.GetComponentInParent<PlayerInfo>() != null) continue;
                rb.AddExplosionForce(
                    explosionForce, explosionPos, explosionRadius, 0.5f, ForceMode.VelocityChange
                );
            }

            ServerEndSession(explosionPos, explosionForce, explosionRadius);
        }

        // =====================================================================
        //  Server — session lifecycle
        // =====================================================================

        private void ServerEndSession(
            Vector3 explosionPos,
            float explosionForce,
            float explosionRadius
        )
        {
            if (!_serverSessionActive)
                return;

            _serverSessionActive = false;

            // Consume item via the slot index cached at session start.
            if (_wielderLinkerSlot >= 0)
            {
                var inventory = GetComponent<PlayerInventory>();
                if (inventory != null)
                {
                    ItemHelper.SetCurrentItemUse(inventory, ItemUseType.Regular);
                    ItemHelper.DecrementAndRemove(inventory, _wielderLinkerSlot);
                    ItemHelper.SetCurrentItemUse(inventory, ItemUseType.None);
                }
            }
            _wielderLinkerSlot = -1;

            // Send the shared disconnect message (VictimNetId identifies the target).
            NetworkServer.SendToAll(
                new RocketVictimDisconnectedMessage
                {
                    VictimNetId       = _targetInfo.GetComponent<NetworkIdentity>().netId,
                    ExplosionPosition = explosionPos,
                    ExplosionForce    = explosionForce,
                    ExplosionRadius   = explosionRadius,
                }
            );

            GlobalSessionLock<RocketTetherNetworkBridge>.Release();

            if (_serverTimeout != null)
            {
                StopCoroutine(_serverTimeout);
                _serverTimeout = null;
            }

            _wielderConn = null;
            _targetInfo  = null;
        }

        // =====================================================================
        //  Client — message handler (Rocket Tether–specific, lock-on flow only)
        // =====================================================================

        /// Called only on the wielder's client when the session lock is held by another player.
        public static void HandleRocketTetherBusy(RocketTetherBusyMessage msg)
        {
            IssaPluginPlugin.Log.LogInfo(
                "[RocketTether] Session busy — another Rocket Tether is already active."
            );
            RocketTetherOverlay.Instance?.ShowBusy();
        }

        // =====================================================================
        //  Hole cleanup
        // =====================================================================

        public override void ServerHoleCleanup()
        {
            if (_serverSessionActive)
            {
                // Use the server-recorded start time for an accurate elapsed value.
                float elapsed    = Time.time - _serverSessionStartTime;
                Vector3 explPos  = _serverRocketStartPos + Vector3.up * _serverRocketSpeed * elapsed;
                ServerEndSession(
                    explPos,
                    ModConfig.RocketTether.ExplosionForce.Value,
                    ModConfig.RocketTether.ExplosionRadius.Value
                );
            }

            if (_serverTimeout != null)
            {
                StopCoroutine(_serverTimeout);
                _serverTimeout = null;
            }
        }

        public override void ClientHoleCleanup()
        {
            RocketTetherClientLogic.ClearAll();
        }
    }
}
