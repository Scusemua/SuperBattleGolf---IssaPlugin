using UnityEngine;

namespace IssaPlugin.Items
{
    /// <summary>
    /// Server-side MonoBehaviour added dynamically to the UFO GameObject after spawn.
    /// Drives terrain-following altitude and smooth yaw rotation toward the movement
    /// direction.  All values are set by UFONetworkBridge from incoming UFOMoveMessages.
    /// NetworkTransform syncs the resulting position to clients each frame.
    /// </summary>
    public class UFOFlyBehaviour : MonoBehaviour
    {
        /// Normalised world-space horizontal direction set each frame by the bridge.
        /// Vector3.zero = no movement.
        public Vector3 MoveInput;

        private Rigidbody _rb;

        private void Start()
        {
            _rb = GetComponent<Rigidbody>();
            if (_rb != null)
            {
                _rb.useGravity = false;
                _rb.freezeRotation = true; // rotation is driven by transform, not physics
            }
        }

        private void FixedUpdate()
        {
            if (_rb == null)
                return;

            float speed = Configuration.UFOSpeed.Value;
            float targetAltitude = Configuration.UFOAltitude.Value;
            float followSpeed = Configuration.UFOTerrainFollowSpeed.Value;

            // ── Terrain-following Y ──────────────────────────────────────────
            float targetY;
            if (
                Physics.Raycast(
                    transform.position,
                    Vector3.down,
                    out RaycastHit hit,
                    2000f,
                    ItemHelper.GroundLayerMask
                )
            )
                targetY = hit.point.y + targetAltitude;
            else
                targetY = transform.position.y; // no terrain below — hold current height

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
                    Configuration.UFOTurnSpeed.Value * Time.fixedDeltaTime
                );
            }
        }
    }
}
