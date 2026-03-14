using UnityEngine;

namespace IssaPlugin.Items
{
    public class UFOCrashBehaviour : MonoBehaviour
    {
        /// Assigned by UFONetworkBridge immediately after AddComponent so
        /// the velocity-direction raycast can read the current linear velocity.
        public Rigidbody Rigidbody;

        public UFONetworkBridge UFONetworkBridge;

        private bool _impacted;
        private float _lifetime;

        /// How close to terrain (in units) the UFO must be for the downward
        /// raycast to trigger the explosion.
        private const float ImpactProximity = 2f;

        /// Fallback: destroy the object if it never reaches terrain.
        private const float MaxLifetime = 15f;

        private void FixedUpdate()
        {
            if (_impacted)
                return;

            _lifetime += Time.fixedDeltaTime;

            if (_lifetime >= MaxLifetime)
            {
                Impact();
                return;
            }

            // 1. Downward proximity check — catches terrain at any height.
            if (Physics.Raycast(transform.position, Vector3.down, ImpactProximity))
            {
                Impact();
                return;
            }

            // 2. Velocity-direction lookahead — prevents tunnelling when falling fast.
            if (Rigidbody != null)
            {
                Vector3 vel = Rigidbody.linearVelocity;
                float speed = vel.magnitude;
                if (speed > 0.1f && vel.y < 0f) // only cast when moving downward
                {
                    float lookahead = speed * Time.fixedDeltaTime + ImpactProximity;
                    if (Physics.Raycast(transform.position, vel.normalized, lookahead))
                    {
                        Impact();
                        return;
                    }
                }
            }

            // 3. Sea-level fallback for flat maps with terrain at y = 0.
            if (transform.position.y <= 0f)
                Impact();
        }

        private void Impact()
        {
            if (_impacted)
                return;

            _impacted = true;

            IssaPluginPlugin.Log.LogInfo($"[UFOCrash] Impact at {transform.position}.");

            VfxManager.PlayPooledVfxLocalOnly(
                VfxType.RocketLauncherRocketExplosion,
                transform.position,
                Quaternion.identity,
                Vector3.one * Configuration.AC130MaydayExplosionScale.Value
            );

            CameraModuleController.Shake(
                GameManager.CameraGameplaySettings.RocketExplosionScreenshakeSettings,
                transform.position
            );

            UFONetworkBridge.ServerEndUFO();
            Destroy(gameObject);
        }
    }
}
