using Mirror;
using UnityEngine;

namespace IssaPlugin.Items
{
    /// Attached server-side to a Rocket when a player fires the rocket launcher
    /// while locked onto a custom item. Steers the rocket toward the item
    /// Transform each FixedUpdate using the same RotateTowards logic the base
    /// game uses for player-to-player homing.
    ///
    /// Mirrors the rocket's velocity direction only — speed is preserved.
    /// Destroyed automatically when the rocket's GameObject is destroyed.
    public class RocketHomingBehaviour : MonoBehaviour
    {
        /// The item Transform to home toward. Set immediately after AddComponent.
        public Transform Target;

        private Rigidbody _rb;

        private void Start()
        {
            _rb = GetComponent<Rigidbody>();
            if (_rb == null)
                IssaPluginPlugin.Log.LogWarning("[RocketHomingBehaviour] No Rigidbody on rocket.");
        }

        private void FixedUpdate()
        {
            if (!NetworkServer.active)
                return;
            if (_rb == null || Target == null)
                return;

            Vector3 toTarget = Target.position - _rb.worldCenterOfMass;
            float distToTarget = toTarget.magnitude;

            // The custom item may not have a physics collider,
            // in which case the rocket never collides with it.
            // Detonate via proximity fuse when close enough.
            float proximityFuse = Configuration.AC130RocketProximityFuse.Value;
            if (distToTarget <= proximityFuse)
            {
                var rocket = GetComponent<Rocket>();
                if (rocket != null)
                    PredatorMissileItem.ServerExplode(rocket);
                return;
            }

            if (distToTarget < 0.01f)
                return;

            // Mirror the game's own homing: rotate the velocity vector toward the
            // target. Use the game's RocketMaxVelocityRotationPerSecond setting so
            // the feel matches normal homing rockets.
            float maxRadiansDelta =
                GameManager.ItemSettings.RocketMaxVelocityRotationPerSecond
                * Time.fixedDeltaTime
                * Mathf.Deg2Rad;

            _rb.linearVelocity = Vector3.RotateTowards(
                _rb.linearVelocity,
                toTarget.normalized * _rb.linearVelocity.magnitude,
                maxRadiansDelta,
                0f
            );
        }
    }
}
