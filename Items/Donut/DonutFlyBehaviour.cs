using UnityEngine;

namespace IssaPlugin.Items
{
    /// <summary>
    /// Server-side MonoBehaviour added dynamically to the Donut GameObject after spawn.
    /// Drives terrain-following altitude and smooth yaw rotation toward the movement
    /// direction.  All values are set by DonutNetworkBridge from incoming DonutMoveMessages.
    /// NetworkTransform syncs the resulting position to clients each frame.
    /// </summary>
    public class DonutFlyBehaviour : CustomHittable
    {
        /// Normalised world-space horizontal direction set each frame by the bridge.
        /// Vector3.zero = no movement.
        public Vector3 MoveInput;

        private Rigidbody _rb;

        public GameObject DonutLaserTarget;

        private void Start()
        {
            _rb = GetComponent<Rigidbody>();
            if (_rb != null)
            {
                _rb.useGravity = false;
                _rb.freezeRotation = true; // rotation is driven by transform, not physics
            }

            gameObject.layer = LayerMask.NameToLayer("Donut");

            DonutLaserTarget = new GameObject("Donut_LaserTarget");
            DonutLaserTarget.transform.SetParent(transform);
        }

        private void FixedUpdate()
        {
            if (_rb == null)
                return;

            float speed = Configuration.DonutSpeed.Value;
            float targetAltitude = Configuration.DonutAltitude.Value;
            float followSpeed = Configuration.DonutTerrainFollowSpeed.Value;

            // ── Terrain-following Y ──────────────────────────────────────────
            // Start 100 units above the Donut so the ray cannot hit the Donut's own
            // colliders, then extend the max distance by the same offset.
            float targetY;
            Vector3 rayOrigin = transform.position + Vector3.down * 5f;
            if (
                Physics.Raycast(
                    rayOrigin,
                    Vector3.down,
                    out RaycastHit hit,
                    2100f,
                    ItemHelper.GroundLayerMask
                )
            )
            {
                targetY = hit.point.y + targetAltitude;
                DonutLaserTarget.transform.position = hit.point;
            }
            else
            {
                targetY = targetAltitude; // no terrain below — hold current height
            }

            float yVelocity = (targetY - transform.position.y) * followSpeed;

            // ── Horizontal velocity from player input ────────────────────────
            Vector3 horizVel = new Vector3(MoveInput.x, 0f, MoveInput.z) * speed;

            _rb.linearVelocity = new Vector3(horizVel.x, yVelocity, horizVel.z);

            // ── Yaw toward movement direction ────────────────────────────────
            if (MoveInput.sqrMagnitude > 0.01f)
            {
                Quaternion targetRot = Quaternion.LookRotation(
                    new Vector3(MoveInput.x, 0f, MoveInput.z),
                    Vector3.up
                );
                transform.rotation = Quaternion.Slerp(
                    transform.rotation,
                    targetRot,
                    Configuration.DonutTurnSpeed.Value * Time.fixedDeltaTime
                );
            }
        }
    }
}
