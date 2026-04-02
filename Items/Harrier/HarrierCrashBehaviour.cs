using Mirror;
using UnityEngine;

namespace IssaPlugin.Items
{
    /// <summary>
    /// Added server-side to the Harrier Jet GameObject when it is shot down.
    ///
    /// On Start:
    ///   - Disables HarrierBehaviour so it stops calling MovePosition.
    ///   - Releases the Rigidbody (non-kinematic, gravity on) and applies an
    ///     initial velocity so the jet tumbles visibly toward the ground.
    ///
    /// NetworkTransform on the prefab syncs the physics-driven fall to all clients
    /// automatically — no separate client-side visual is needed.
    ///
    /// On ground impact: spawns an explosion rocket at the crash point, then sets
    /// IsComplete so the server coroutine can call NetworkServer.Destroy.
    /// </summary>
    public class HarrierCrashBehaviour : MonoBehaviour
    {
        // ================================================================
        //  Set by HarrierNetworkBridge before the component runs
        // ================================================================

        public PlayerInventory ThrowerInventory;

        /// Direction from the crash site toward the killing rocket's explosion.
        /// Used to add a lateral shove in the hit direction.
        public Vector3 KillingRocketDir;

        public float ExplosionScale;

        // ================================================================
        //  Public state read by HarrierNetworkBridge
        // ================================================================

        public bool IsComplete { get; private set; }

        // ================================================================
        //  Internal state
        // ================================================================

        private Rigidbody _rb;
        private bool _impacted;
        private float _lifetime;

        private const float MaxLifetime      = 20f;
        private const float ImpactProximity  = 3f;
        private const float InitialDownSpeed = 15f;
        private const float RocketImpulse    = 25f;

        // ----------------------------------------------------------------
        //  Smoke trail (all clients)
        // ----------------------------------------------------------------

        private GameObject _smokeTrail;
        private GameObject _fireTrail;

        // ================================================================
        //  Unity lifecycle
        // ================================================================

        private void Start()
        {
            // Smoke trail — all clients spawn it locally (purely visual).
            if (AssetLoader.MaydaySmokeTrailPrefab != null)
            {
                _smokeTrail = Instantiate(
                    AssetLoader.MaydaySmokeTrailPrefab,
                    transform.position,
                    Quaternion.identity
                );
                _smokeTrail.transform.SetParent(transform, worldPositionStays: true);
            }
            else
            {
                IssaPluginPlugin.Log.LogWarning("[Mayday] Smoke trail prefab not loaded.");
            }

            // Smoke trail — all clients spawn it locally (purely visual).
            if (AssetLoader.MaydayFireTrailPrefab != null)
            {
                _fireTrail = Instantiate(
                    AssetLoader.MaydayFireTrailPrefab,
                    transform.position,
                    Quaternion.identity
                );
                _fireTrail.transform.SetParent(transform, worldPositionStays: true);
            }
            else
            {
                IssaPluginPlugin.Log.LogWarning("[Mayday] Fire trail prefab not loaded.");
            }

            // Stop the autonomous flight driver.
            var flyBehaviour = GetComponent<HarrierBehaviour>();
            if (flyBehaviour != null)
                flyBehaviour.enabled = false;

            // Release physics.
            _rb = GetComponent<Rigidbody>();
            if (_rb == null)
                _rb = gameObject.AddComponent<Rigidbody>();
            _rb.isKinematic = false;
            _rb.useGravity  = true;

            // Initial velocity: carry the jet's current forward momentum plus a
            // downward push, then add a nudge from the killing rocket direction.
            Vector3 crashVel = transform.forward * ModConfig.Harrier.ApproachSpeed.Value * 0.125f
                             + Vector3.down * InitialDownSpeed;

            if (KillingRocketDir != Vector3.zero)
                crashVel += KillingRocketDir * RocketImpulse;

            _rb.linearVelocity = crashVel;

            // Add a random tumble so it looks like a stricken aircraft.
            _rb.AddTorque(
                new Vector3(
                    Random.Range(-1f, 1f),
                    Random.Range(-0.5f, 0.5f),
                    Random.Range(-1f, 1f)
                ) * 3f,
                ForceMode.Impulse
            );
        }

        private void FixedUpdate()
        {
            // Only run on server; clients follow via NetworkTransform.
            if (!NetworkServer.active || _impacted)
                return;

            _lifetime += Time.fixedDeltaTime;

            if (_lifetime >= MaxLifetime)
            {
                Impact();
                return;
            }

            // 1. Downward proximity raycast — catches terrain at any altitude.
            if (Physics.Raycast(transform.position, Vector3.down, ImpactProximity))
            {
                Impact();
                return;
            }

            // 2. Velocity-direction lookahead — prevents tunnelling at high speed.
            if (_rb != null)
            {
                Vector3 vel   = _rb.linearVelocity;
                float   speed = vel.magnitude;
                if (speed > 0.1f && vel.y < 0f)
                {
                    float lookahead = speed * Time.fixedDeltaTime + ImpactProximity;
                    if (Physics.Raycast(transform.position, vel.normalized, lookahead))
                    {
                        Impact();
                        return;
                    }
                }
            }

            // 3. Sea-level fallback for flat maps.
            if (transform.position.y <= 0f)
                Impact();
        }

        // ================================================================
        //  Impact
        // ================================================================

        private void Impact()
        {
            if (_impacted)
                return;

            _impacted  = true;
            IsComplete = true;

            IssaPluginPlugin.Log.LogInfo(
                $"[HarrierCrash] Impact at {transform.position:F0}."
            );

            if (ThrowerInventory != null)
                HarrierItem.SpawnAndExplodeImpactRocket(
                    ThrowerInventory,
                    transform.position,
                    ExplosionScale
                );
            
            VfxManager.PlayPooledVfxLocalOnly(
                VfxType.RocketLauncherRocketExplosion,
                transform.position,
                Quaternion.identity,
                Vector3.one * ModConfig.AC130.MaydayExplosionScale.Value
            );

            CameraModuleController.Shake(
                GameManager.CameraGameplaySettings.RocketExplosionScreenshakeSettings,
                transform.position
            );

            Destroy(gameObject);
        }
    }
}
