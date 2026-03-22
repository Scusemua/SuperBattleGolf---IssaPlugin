using UnityEngine;
using System.Collections;

namespace IssaPlugin.Items {
    public class PlaceableWallDestructionBehaviour : MonoBehaviour {
        public float HealthPoints { get; private set; }

        public float VelocityImpactFactor { get; private set; }

        public float TorsionMultiplier { get; private set; }

        public Rigidbody Rigidbody {get; private set;}

        private void Awake() {
            Rigidbody = GetComponent<Rigidbody>();

            HealthPoints = Configuration.PlaceableWallHealthPoints.Value;
            VelocityImpactFactor = Configuration.PlaceableWallVelocityImpactFactor.Value;
            TorsionMultiplier = Configuration.PlaceableWallTorsionMultiplier.Value;
        }

        private void TryDeformWall()
        {
            if (HealthPoints > 0)
            {
                return;
            }

            foreach(Transform childTransform in transform) {
                Rigidbody spawnedRigidbody = childTransform.gameObject.AddComponent<Rigidbody>();
                childTransform.parent = null;

                // Transfer the impact velocity to the new rigidbody.
                spawnedRigidbody.linearVelocity = GetComponent<Rigidbody>().GetPointVelocity(childTransform.position);

                // Transfer the torque velocity to the new rigidbody.
                spawnedRigidbody.AddTorque(GetComponent<Rigidbody>().angularVelocity, ForceMode.VelocityChange);
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