using UnityEngine;
using System.Collections;

namespace IssaPlugin.Items {
    public class PlaceableWallDestructionBehaviour : MonoBehaviour {
        public float HealthPoints { get; private set; }

        public float VelocityImpactFactor { get; private set; }

        public float TorsionMultiplier { get; private set; }

        public Rigidbody Rigidbody {get; private set;}

        private bool _deformed;

        private void Awake() {
            Rigidbody = GetComponent<Rigidbody>();

            HealthPoints = Configuration.PlaceableWallHealthPoints.Value;
            VelocityImpactFactor = Configuration.PlaceableWallVelocityImpactFactor.Value;
            TorsionMultiplier = Configuration.PlaceableWallTorsionMultiplier.Value;
        }

        public void ApplyDamage(float amount)
        {
            HealthPoints -= amount;
            TryDeformWall();
        }

        public void ApplyExplosionDamage(Vector3 explosionCenter, float explosionRadius)
        {
            HealthPoints = 0f;
            TryDeformWall(explosionCenter, explosionRadius);
        }

        private void TryDeformWall(Vector3? explosionCenter = null, float explosionRadius = 0f)
        {
            if (_deformed || HealthPoints > 0)
                return;

            _deformed = true;

            float debrisLifetime = Configuration.PlaceableWallDebrisLifetime.Value;
            float explosionForce = Configuration.PlaceableWallRocketExplosionForce.Value;

            foreach(Transform childTransform in transform) {
                childTransform.parent = null;
                Rigidbody spawnedRigidbody = childTransform.gameObject.AddComponent<Rigidbody>();

                if (explosionCenter.HasValue)
                {
                    // Apply radial blast force from the rocket's world position.
                    spawnedRigidbody.AddExplosionForce(
                        explosionForce,
                        explosionCenter.Value,
                        explosionRadius,
                        1f,
                        ForceMode.Impulse
                    );
                }
                else
                {
                    // Transfer the parent chunk's velocity (e.g. from a melee hit).
                    spawnedRigidbody.linearVelocity = Rigidbody.GetPointVelocity(childTransform.position);
                    spawnedRigidbody.AddTorque(Rigidbody.angularVelocity, ForceMode.VelocityChange);
                }

                Destroy(childTransform.gameObject, debrisLifetime);
            }

            // Destroy this chunk of the wall.
            Destroy(gameObject);
        }

        private void FixedUpdate() {
            // Damage the wall based on torsion/torque. Objects that are rotating rapidly will 
            // apply torque to the wall, resulting in torsion damage, or something like that.
            // I'm not a physicist.
            float damage = TorsionMultiplier * Rigidbody.angularVelocity.sqrMagnitude;
            HealthPoints -= damage; 

            TryDeformWall();
        }

        // When the chunk hits another object, take some of its health away
        void OnCollisionEnter(Collision collision) {
            // Players cannot destroy walls simply by walking into them.
            if (collision.gameObject.GetComponentInParent<PlayerInfo>() != null)
            {
                return;
            }

            float relativeVelocity = collision.relativeVelocity.sqrMagnitude;

            // If the chunk was hit by a rigidbody, multiply the damage by its mass
            float damage = relativeVelocity * VelocityImpactFactor;
            if (collision.rigidbody) {
                // If this chunk of the wall was hit by another Rigidbody,
                // then multiply the damage by the mass of the other Rigidbody.
                damage *= collision.rigidbody.mass;
            }

            HealthPoints -= damage;
        }
    }
}