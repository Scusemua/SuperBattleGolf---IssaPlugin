using System.Collections.Generic;
using Mirror;
using UnityEngine;

namespace IssaPlugin.Items
{
    /// <summary>
    /// Server-only MonoBehaviour attached to the thrown grenade after
    /// NetworkServer.Spawn(). Clients receive a visual replica but do NOT
    /// have this component — it is added via AddComponent after Spawn so Mirror
    /// never sends it to clients.
    ///
    /// Phase 1 — Flying: Rigidbody propels the grenade as a ballistic arc.
    ///   A 0.25 s grace timer prevents immediate detonation on the thrower's
    ///   own colliders.
    ///
    /// Phase 2 — Landed: OnCollisionEnter triggers Land(), which runs a
    ///   server-side Physics.OverlapSphere to find victims, then delegates
    ///   to the thrower's RocketTetherGrenadeNetworkBridge.
    ///   The grenade object is destroyed immediately after.
    /// </summary>
    public class RocketTetherGrenadeBehaviour : MonoBehaviour
    {
        /// NetId of the player who threw the grenade (set by the bridge before first tick).
        public uint ThrowerNetId;

        private Rigidbody _rb;
        private bool _landed;

        // Seconds before collision detection is enabled.
        // Prevents the grenade from detonating on the thrower's own colliders
        // in the frame it spawns.
        private float _graceTimer = 0.25f;

        private void Start()
        {
            _rb = GetComponent<Rigidbody>();
        }

        private void FixedUpdate()
        {
            if (_graceTimer > 0f)
                _graceTimer -= Time.fixedDeltaTime;
        }

        // Guard both _landed and _graceTimer here so Land() is never reached
        // from spurious or simultaneous collision events.
        private void OnCollisionEnter(Collision collision)
        {
            if (_landed || _graceTimer > 0f)
                return;
            Land();
        }

        private void Land()
        {
            // Defensive isServer guard — this component is only ever added on the
            // server, but the check costs nothing and makes the intent explicit.
            if (!NetworkServer.active)
                return;

            _landed = true;

            if (_rb != null)
            {
                _rb.isKinematic     = true;
                _rb.linearVelocity  = Vector3.zero;
                _rb.angularVelocity = Vector3.zero;
            }

            ServerApplyTethers(transform.position);
            NetworkServer.Destroy(gameObject);
        }

        // Instance method — uses this.ThrowerNetId.
        private void ServerApplyTethers(Vector3 landPos)
        {
            if (!NetworkServer.spawned.TryGetValue(ThrowerNetId, out var throwerIdentity))
            {
                IssaPluginPlugin.Log.LogWarning(
                    $"[RocketTetherGrenade] Thrower netId={ThrowerNetId} not found — grenade fizzles."
                );
                return;
            }

            var bridge = throwerIdentity.GetComponent<RocketTetherGrenadeNetworkBridge>();
            if (bridge == null)
            {
                IssaPluginPlugin.Log.LogWarning(
                    "[RocketTetherGrenade] Thrower has no RocketTetherGrenadeNetworkBridge."
                );
                return;
            }

            float blastRadius = ModConfig.RocketTetherGrenade.BlastRadius.Value;
            int   maxVictims  = ModConfig.RocketTetherGrenade.MaxVictims.Value;

            var victims = new List<PlayerInfo>(maxVictims);

            foreach (var col in Physics.OverlapSphere(landPos, blastRadius))
            {
                if (victims.Count >= maxVictims)
                    break;

                var info = col.GetComponentInParent<PlayerInfo>();
                if (info == null)
                    continue;

                var nid = info.GetComponent<NetworkIdentity>();
                if (nid == null)
                    continue;

                // Skip the thrower.
                if (nid.netId == ThrowerNetId)
                    continue;

                // Skip already-queued players (a player with many colliders might
                // appear multiple times in the OverlapSphere results).
                if (victims.Contains(info))
                    continue;

                // Electromagnetic shield blocks the tether; play the shield hit VFX.
                if (info.IsElectromagnetShieldActive)
                {
                    Vector3 hitDir = (info.transform.position - landPos).normalized;
                    info.PlayElectromagnetShieldHitForAllClients(hitDir);
                    continue;
                }

                victims.Add(info);
            }

            if (victims.Count == 0)
            {
                IssaPluginPlugin.Log.LogInfo("[RocketTetherGrenade] Landed — no valid victims in radius.");
                return;
            }

            IssaPluginPlugin.Log.LogInfo(
                $"[RocketTetherGrenade] Landed — tethering {victims.Count} victim(s)."
            );
            bridge.ServerBeginVictimTethers(victims);
        }
    }
}
