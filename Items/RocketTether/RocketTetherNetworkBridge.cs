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
        private uint _targetNetId;
        private Coroutine _serverTimeout;

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

            // ── 2. Validate wielder has Rocket Tether ──────────────────────
            var inventory = GetComponent<PlayerInventory>();
            var equipped = inventory?.GetEffectivelyEquippedItem(true);
            if (equipped != ItemRegistry.RocketTetherItemType)
            {
                IssaPluginPlugin.Log.LogWarning(
                    "[RocketTether] Server: wielder does not have Rocket Tether."
                );
                GlobalSessionLock<RocketTetherNetworkBridge>.Release(this);
                return;
            }

            // ── 3. Validate target — players only, no self-link ───────────────
            if (!NetworkServer.spawned.TryGetValue(msg.TargetNetId, out var targetIdentity))
            {
                IssaPluginPlugin.Log.LogWarning(
                    $"[RocketTether] Server: target netId={msg.TargetNetId} not found."
                );
                GlobalSessionLock<RocketTetherNetworkBridge>.Release(this);
                return;
            }

            var targetInfo = targetIdentity.GetComponentInParent<PlayerInfo>();
            if (targetInfo == null)
            {
                IssaPluginPlugin.Log.LogWarning("[RocketTether] Server: target is not a player.");
                GlobalSessionLock<RocketTetherNetworkBridge>.Release(this);
                return;
            }

            if (targetIdentity == GetComponent<NetworkIdentity>())
            {
                IssaPluginPlugin.Log.LogWarning("[RocketTether] Server: self-targeting rejected.");
                GlobalSessionLock<RocketTetherNetworkBridge>.Release(this);
                return;
            }

            // ── 3b. Electromagnetic shield check ─────────────────────────────
            if (targetInfo.IsElectromagnetShieldActive)
            {
                IssaPluginPlugin.Log.LogInfo(
                    "[RocketTether] Server: target has electromagnetic shield — rejecting."
                );
                GlobalSessionLock<RocketTetherNetworkBridge>.Release(this);
                targetInfo.PlayElectromagnetShieldHitForAllClients(
                    (targetInfo.transform.position - transform.position).normalized
                );
                return;
            }

            // ── 4. All validation passed — consume item ──────────────────────
            ItemHelper.ConsumeEquippedItem(inventory);

            var wielderNetId = GetComponent<NetworkIdentity>().netId;

            // ── 5. Compute rocket spawn position ──────────────────────────────
            float rocketSpeed = ModConfig.RocketTether.RocketSpeed.Value;
            float duration    = ModConfig.RocketTether.TetherDuration.Value;
            Vector3 rocketStart = targetInfo.transform.position + Vector3.up * 1.5f;

            // ── 6. Store server session state ─────────────────────────────────
            _serverSessionActive    = true;
            _targetNetId            = targetIdentity.netId;
            _serverRocketStartPos   = rocketStart;
            _serverRocketSpeed      = rocketSpeed;
            _serverSessionStartTime = Time.time;

            IssaPluginPlugin.Log.LogInfo(
                $"[RocketTether] Server: session started. wielder={wielderNetId} target={targetIdentity.netId}"
            );

            // ── 7. Broadcast to all clients using the shared message ──────────
            bool connectedMessageSent = false;
            try
            {
                NetworkServer.SendToAll(
                    new RocketVictimConnectedMessage
                    {
                        WielderNetId   = wielderNetId,
                        VictimNetId    = _targetNetId,
                        RocketStartPos = rocketStart,
                        RocketSpeed    = rocketSpeed,
                        Duration       = duration,
                        SpringForce    = ModConfig.RocketTether.SpringForce.Value,
                        MaxPullSpeed   = ModConfig.RocketTether.MaxPullSpeed.Value,
                        NaturalLength  = ModConfig.RocketTether.NaturalLength.Value,
                    }
                );
                connectedMessageSent = true;

                // ── 8. Start server timeout / explosion coroutine ─────────────
                _serverTimeout = StartCoroutine(ServerRocketCoroutine(duration));
            }
            catch (System.Exception ex)
            {
                IssaPluginPlugin.Log.LogError(
                    $"[RocketTether] Failed to start server session: {ex}"
                );
                ServerEndSession(
                    rocketStart,
                    0f,
                    0f,
                    broadcastDisconnect: connectedMessageSent
                );
            }
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

            try
            {
                // Apply explosion to non-player Rigidbodies.
                // Player physics are client-authoritative; clients handle their own
                // forces via RocketVictimDisconnectedMessage.
                var colliders = Physics.OverlapSphere(explosionPos, explosionRadius);
                foreach (var col in colliders)
                {
                    if (col == null)
                        continue;

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
            }
            catch (System.Exception ex)
            {
                // Teardown below must still run; otherwise a physics exception leaves
                // the process-wide session lock held forever.
                IssaPluginPlugin.Log.LogError(
                    $"[RocketTether] Explosion physics failed; ending session safely: {ex}"
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
            float explosionRadius,
            bool broadcastDisconnect = true
        )
        {
            if (!_serverSessionActive)
            {
                // Defensive recovery for a partially torn-down session.
                GlobalSessionLock<RocketTetherNetworkBridge>.Release(this);
                return;
            }

            _serverSessionActive = false;
            uint victimNetId = _targetNetId;
            Coroutine timeout = _serverTimeout;
            _serverTimeout = null;

            try
            {
                if (timeout != null)
                {
                    try
                    {
                        StopCoroutine(timeout);
                    }
                    catch (System.Exception ex)
                    {
                        IssaPluginPlugin.Log.LogWarning(
                            $"[RocketTether] Could not stop timeout coroutine: {ex}"
                        );
                    }
                }

                // The cached netId remains valid even if the victim has disconnected
                // and its PlayerInfo/NetworkIdentity has already been destroyed.
                if (
                    broadcastDisconnect
                    && NetworkServer.active
                    && victimNetId != 0u
                )
                {
                    try
                    {
                        NetworkServer.SendToAll(
                            new RocketVictimDisconnectedMessage
                            {
                                VictimNetId       = victimNetId,
                                ExplosionPosition = explosionPos,
                                ExplosionForce    = explosionForce,
                                ExplosionRadius   = explosionRadius,
                            }
                        );
                    }
                    catch (System.Exception ex)
                    {
                        IssaPluginPlugin.Log.LogError(
                            $"[RocketTether] Disconnect broadcast failed during teardown: {ex}"
                        );
                    }
                }
            }
            finally
            {
                _targetNetId = 0u;
                GlobalSessionLock<RocketTetherNetworkBridge>.Release(this);
            }
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

        private void ServerEndCurrentSession()
        {
            float elapsed = Mathf.Max(0f, Time.time - _serverSessionStartTime);
            Vector3 explosionPos =
                _serverRocketStartPos + Vector3.up * _serverRocketSpeed * elapsed;
            ServerEndSession(
                explosionPos,
                ModConfig.RocketTether.ExplosionForce.Value,
                ModConfig.RocketTether.ExplosionRadius.Value
            );
        }

        public override void ServerHoleCleanup()
        {
            if (_serverSessionActive)
                ServerEndCurrentSession();

            if (_serverTimeout != null)
            {
                StopCoroutine(_serverTimeout);
                _serverTimeout = null;
            }
        }

        public override void OnStopServer()
        {
            var holder = GlobalSessionLock<RocketTetherNetworkBridge>.Holder;
            if (holder == null)
                return;

            if (ReferenceEquals(holder, this))
            {
                IssaPluginPlugin.Log.LogInfo(
                    "[RocketTether] Wielder disconnected during session — forcing cleanup."
                );

                if (_serverSessionActive)
                    ServerEndCurrentSession();
                else
                    GlobalSessionLock<RocketTetherNetworkBridge>.Release(this);
                return;
            }

            // Every player owns a bridge, so the disconnect callback on the victim's
            // bridge can immediately terminate the session held by the wielder's bridge.
            if (holder._serverSessionActive && holder._targetNetId == netId)
            {
                IssaPluginPlugin.Log.LogInfo(
                    "[RocketTether] Victim disconnected during session — forcing cleanup."
                );
                holder.ServerEndCurrentSession();
            }
        }

        public override void ClientHoleCleanup()
        {
            RocketTetherClientLogic.ClearAll();
        }
    }
}
