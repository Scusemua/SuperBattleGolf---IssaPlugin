using System.Collections.Generic;
using Mirror;
using UnityEngine;

namespace IssaPlugin.Items
{
    /// <summary>
    /// Added at runtime only to the LOCAL shooter's flame particle instance.
    /// Remote clients' copies of the same particles receive no hit detector, so
    /// burn request messages are only ever sent by the player who is actually firing.
    ///
    /// OnTriggerStay fires each physics tick while a collider overlaps the flame.
    /// We rate-limit the per-victim burn-request sends so the server is not spammed
    /// every FixedUpdate while the flame is touching someone.
    /// </summary>
    public class FlamethrowerHitDetector : MonoBehaviour
    {
        private uint _shooterNetId;
        private FlamethrowerNetworkBridge _bridge;

        // Tracks the last time a BurnRequest was sent for each victim netId.
        private readonly Dictionary<uint, float> _lastSendTime = new();

        /// <summary>
        /// Must be called immediately after the component is added so the detector
        /// knows which player is firing and can exclude them from hits.
        /// </summary>
        public void Initialize(FlamethrowerNetworkBridge bridge, uint shooterNetId)
        {
            _bridge = bridge;
            _shooterNetId = shooterNetId;
        }

        private void OnTriggerStay(Collider other)
        {
            if (_bridge == null)
                return;

            // Walk up the hierarchy looking for a PlayerMovement (players are
            // typically hierarchical: collider child → player root with components).
            var movement = other.GetComponentInParent<PlayerMovement>();
            if (movement == null)
                return;

            var identity = movement.GetComponent<NetworkIdentity>();
            if (identity == null)
                return;

            uint victimNetId = identity.netId;

            // Never burn yourself.
            if (victimNetId == _shooterNetId)
                return;

            float interval = ModConfig.Flamethrower.BurnRequestInterval.Value;
            float now = Time.time;

            if (_lastSendTime.TryGetValue(victimNetId, out float last) && now - last < interval)
                return;

            _lastSendTime[victimNetId] = now;
            _bridge.ClientRequestBurn(victimNetId);
        }

        /// <summary>
        /// Clear per-victim cooldowns when firing stops so the next session can
        /// immediately hit targets that were recently in range.
        /// </summary>
        public void ResetCooldowns() => _lastSendTime.Clear();
    }
}
