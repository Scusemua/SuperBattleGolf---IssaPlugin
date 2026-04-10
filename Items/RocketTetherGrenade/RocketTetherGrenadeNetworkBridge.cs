using System.Collections;
using System.Collections.Generic;
using Mirror;
using UnityEngine;

namespace IssaPlugin.Items
{
    /// <summary>
    /// Attached to every player object via NetworkBridgePatches.
    ///
    /// Server side:  Validates the throw, consumes the item, spawns the grenade.
    ///               After landing, manages per-victim server coroutines and
    ///               broadcasts RocketVictimConnected/Disconnected messages via
    ///               the shared message types.
    ///
    /// Client side:  Derives throw velocity from the camera and sends
    ///               RocketTetherGrenadeThrowMessage.  All client-side VFX and
    ///               spring physics are handled by RocketTetherClientLogic (shared
    ///               with the single-target Rocket Tether item).
    ///
    /// There is no GlobalSessionLock — multiple grenades can be in flight and
    /// active simultaneously from different players.
    /// </summary>
    public class RocketTetherGrenadeNetworkBridge : NetworkBridgeBase
    {
        // =====================================================================
        //  Server-side per-victim tracking
        //
        //  One consolidated dictionary instead of three separate ones.
        //  All per-victim fields needed by ServerHoleCleanup are in one place.
        // =====================================================================

        private struct ServerVictimSession
        {
            public Coroutine Coroutine;
            public Vector3 RocketStartPos;
            public float RocketSpeed;
            public float StartTime; // Time.time when the session began
        }

        private readonly Dictionary<uint, ServerVictimSession> _serverVictimSessions =
            new Dictionary<uint, ServerVictimSession>();

        // =====================================================================
        //  Client — throwing the grenade
        // =====================================================================

        /// Called from RocketTetherGrenadeItemDefinition.OnUse on the local client.
        public void ClientThrow()
        {
            if (!isOwned)
                return;

            var cam = Camera.main;
            if (cam == null)
            {
                IssaPluginPlugin.Log.LogWarning("[RTGrenade] No main camera for throw direction.");
                return;
            }

            Vector3 forward = cam.transform.forward;
            Vector3 throwOrigin = cam.transform.position + forward * 1.2f + Vector3.up * 0.3f;
            Vector3 throwDir = (
                forward + Vector3.up * ModConfig.RocketTetherGrenade.LobAngle.Value
            ).normalized;
            Vector3 velocity = throwDir * ModConfig.RocketTetherGrenade.ThrowSpeed.Value;

            NetworkClient.Send(
                new RocketTetherGrenadeThrowMessage
                {
                    ThrowOrigin = throwOrigin,
                    ThrowVelocity = velocity,
                }
            );

            IssaPluginPlugin.Log.LogInfo(
                $"[RTGrenade] Client throw: origin={throwOrigin} speed={velocity.magnitude:F1}"
            );
        }

        public static void ClientHandleLand(Vector3 position, uint throwerNetId)
        {
            var vfxPrefab = AssetLoader.RocketTetherGrenadeExplosionVfx;
            if (vfxPrefab != null)
            {
                var splash = Object.Instantiate(vfxPrefab, position, Quaternion.identity);
                Object.Destroy(splash, 5f);
            }
        }

        // =====================================================================
        //  Server — handling the throw request
        // =====================================================================

        public void ServerHandleThrow(Vector3 throwOrigin, Vector3 throwVelocity)
        {
            if (!isServer)
                return;

            var inventory = GetComponent<PlayerInventory>();
            if (inventory == null)
            {
                IssaPluginPlugin.Log.LogError("[RTGrenade] No PlayerInventory on bridge object.");
                return;
            }

            if (
                inventory.GetEffectivelyEquippedItem(true)
                != ItemRegistry.RocketTetherGrenadeItemType
            )
            {
                IssaPluginPlugin.Log.LogWarning(
                    "[RTGrenade] Player does not have RTGrenade equipped."
                );
                return;
            }

            if (AssetLoader.RocketTetherGrenadePrefab == null)
            {
                IssaPluginPlugin.Log.LogError("[RTGrenade] Grenade prefab not loaded.");
                return;
            }

            // Consume the item immediately on throw (same as PoisonJar pattern).
            ItemHelper.ConsumeEquippedItem(inventory);

            // Server-side velocity clamp to prevent speed exploits from modified clients.
            Vector3 velocity = Vector3.ClampMagnitude(
                throwVelocity,
                ModConfig.RocketTetherGrenade.ThrowSpeed.Value * 1.5f
            );

            var grenadeGo = Object.Instantiate(
                AssetLoader.RocketTetherGrenadePrefab,
                throwOrigin,
                velocity.sqrMagnitude > 0.001f
                    ? Quaternion.LookRotation(velocity.normalized, Vector3.up)
                    : Quaternion.identity
            );

            if (grenadeGo == null)
            {
                IssaPluginPlugin.Log.LogError("[RTGrenade] Grenade prefab failed to instantiate.");
                return;
            }

            var rb = grenadeGo.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.useGravity = true;
                rb.isKinematic = false;
                rb.linearVelocity = velocity;

                // Spin axis perpendicular to throw direction for a tumbling effect.
                Vector3 spinAxis = Vector3.Cross(velocity.normalized, Vector3.up).normalized;
                if (spinAxis.sqrMagnitude < 0.01f)
                    spinAxis = Vector3.right;
                rb.angularVelocity = spinAxis * 8f;
            }

            NetworkServer.Spawn(grenadeGo);

            // AddComponent after Spawn so this behaviour is server-only
            // (Mirror does not replicate components added post-spawn).
            var behaviour = grenadeGo.AddComponent<RocketTetherGrenadeBehaviour>();
            behaviour.ThrowerNetId = netId;

            IssaPluginPlugin.Log.LogInfo(
                $"[RTGrenade] Grenade spawned from {throwOrigin} speed={velocity.magnitude:F1} m/s"
            );
        }

        // =====================================================================
        //  Server — applying tethers to AoE victims (called by the behaviour)
        // =====================================================================

        public void ServerBeginVictimTethers(List<PlayerInfo> victims)
        {
            if (!isServer)
                return;

            float rocketSpeed = ModConfig.RocketTetherGrenade.RocketSpeed.Value;
            float duration = ModConfig.RocketTetherGrenade.TetherDuration.Value;

            foreach (var victim in victims)
            {
                var victimIdentity = victim.GetComponent<NetworkIdentity>();
                if (victimIdentity == null)
                    continue;

                uint victimNetId = victimIdentity.netId;

                // If this victim is somehow already tethered (e.g. two grenades landing
                // simultaneously), cancel the existing session first.
                if (_serverVictimSessions.TryGetValue(victimNetId, out var existing))
                {
                    if (existing.Coroutine != null)
                        StopCoroutine(existing.Coroutine);
                    _serverVictimSessions.Remove(victimNetId);
                }

                Vector3 rocketStart = victim.transform.position + Vector3.up * 1.5f;

                // Broadcast the shared Connected message to all clients.
                NetworkServer.SendToAll(
                    new RocketVictimConnectedMessage
                    {
                        WielderNetId = netId,
                        VictimNetId = victimNetId,
                        RocketStartPos = rocketStart,
                        RocketSpeed = rocketSpeed,
                        Duration = duration,
                        SpringForce = ModConfig.RocketTetherGrenade.SpringForce.Value,
                        MaxPullSpeed = ModConfig.RocketTetherGrenade.MaxPullSpeed.Value,
                        NaturalLength = ModConfig.RocketTetherGrenade.NaturalLength.Value,
                    }
                );

                // Start the per-victim server coroutine and record all state needed
                // for ServerHoleCleanup (particularly StartTime for elapsed calculation).
                var session = new ServerVictimSession
                {
                    RocketStartPos = rocketStart,
                    RocketSpeed = rocketSpeed,
                    StartTime = Time.time,
                };

                session.Coroutine = StartCoroutine(
                    ServerVictimTetherCoroutine(victimNetId, session, duration)
                );
                _serverVictimSessions[victimNetId] = session;

                IssaPluginPlugin.Log.LogInfo($"[RTGrenade] Tethered victim netId={victimNetId}.");
            }
        }

        // =====================================================================
        //  Server — per-victim flight and explosion coroutine
        // =====================================================================

        private IEnumerator ServerVictimTetherCoroutine(
            uint victimNetId,
            ServerVictimSession session,
            float duration
        )
        {
            yield return new WaitForSeconds(duration);

            // Session may have already been cleaned up by ServerHoleCleanup.
            if (!_serverVictimSessions.ContainsKey(victimNetId))
                yield break;

            float explosionForce = ModConfig.RocketTetherGrenade.ExplosionForce.Value;
            float explosionRadius = ModConfig.RocketTetherGrenade.ExplosionRadius.Value;
            Vector3 explosionPos =
                session.RocketStartPos + Vector3.up * session.RocketSpeed * duration;

            // Apply explosion to non-player Rigidbodies (same as RocketTetherNetworkBridge).
            foreach (var col in Physics.OverlapSphere(explosionPos, explosionRadius))
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

            NetworkServer.SendToAll(
                new RocketVictimDisconnectedMessage
                {
                    VictimNetId = victimNetId,
                    ExplosionPosition = explosionPos,
                    ExplosionForce = explosionForce,
                    ExplosionRadius = explosionRadius,
                }
            );

            _serverVictimSessions.Remove(victimNetId);

            IssaPluginPlugin.Log.LogInfo(
                $"[RTGrenade] Victim {victimNetId} rocket exploded at {explosionPos}."
            );
        }

        // =====================================================================
        //  Hole cleanup
        // =====================================================================

        public override void ServerHoleCleanup()
        {
            if (!isServer || _serverVictimSessions.Count == 0)
                return;

            // Snapshot the dictionary so we can safely modify it while iterating.
            var snapshot = new KeyValuePair<uint, ServerVictimSession>[_serverVictimSessions.Count];
            int i = 0;
            foreach (var kvp in _serverVictimSessions)
                snapshot[i++] = kvp;

            float explosionForce = ModConfig.RocketTetherGrenade.ExplosionForce.Value;
            float explosionRadius = ModConfig.RocketTetherGrenade.ExplosionRadius.Value;

            foreach (var kvp in snapshot)
            {
                uint victimNetId = kvp.Key;
                ServerVictimSession session = kvp.Value;

                if (session.Coroutine != null)
                    StopCoroutine(session.Coroutine);

                // Compute explosion position using the server-recorded start time.
                float elapsed = Time.time - session.StartTime;
                Vector3 explosionPos =
                    session.RocketStartPos + Vector3.up * session.RocketSpeed * elapsed;

                NetworkServer.SendToAll(
                    new RocketVictimDisconnectedMessage
                    {
                        VictimNetId = victimNetId,
                        ExplosionPosition = explosionPos,
                        ExplosionForce = explosionForce,
                        ExplosionRadius = explosionRadius,
                    }
                );
            }

            _serverVictimSessions.Clear();
        }

        public override void ClientHoleCleanup()
        {
            // RocketTetherNetworkBridge.ClientHoleCleanup() calls
            // RocketTetherClientLogic.ClearAll(), which clears the shared session
            // dictionary covering grenade sessions too. No additional work needed here.
        }
    }
}
