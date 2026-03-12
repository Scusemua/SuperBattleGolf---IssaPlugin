using Mirror;
using UnityEngine;

namespace IssaPlugin.Items
{
    /// <summary>
    /// Attached server-side to a Rocket when a player fires the rocket launcher
    /// while locked onto the AC130 gunship. Steers the rocket toward the gunship
    /// Transform each FixedUpdate using the same RotateTowards logic the base
    /// game uses for player-to-player homing.
    ///
    /// Mirrors the rocket's velocity direction only — speed is preserved.
    /// Destroyed automatically when the rocket's GameObject is destroyed.
    /// </summary>
    public class GunshipHomingBehaviour : MonoBehaviour
    {
        /// <summary>The gunship Transform to home toward. Set immediately after AddComponent.</summary>
        public Transform Target;

        private Rigidbody _rb;

        private void Start()
        {
            _rb = GetComponent<Rigidbody>();
            if (_rb == null)
                IssaPluginPlugin.Log.LogWarning("[GunshipHoming] No Rigidbody on rocket.");
        }

        private void FixedUpdate()
        {
            if (!NetworkServer.active)
                return;
            if (_rb == null || Target == null)
                return;

            Vector3 toTarget = Target.position - _rb.worldCenterOfMass;
            if (toTarget.sqrMagnitude < 0.01f)
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
