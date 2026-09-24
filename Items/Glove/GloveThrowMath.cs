using UnityEngine;

namespace IssaPlugin.Items
{
    /// <summary>
    /// Shared throw velocity math for the Glove.
    ///
    /// Kept free of MonoBehaviour / networking so trajectory preview and the server
    /// throw path share the exact same formula.
    /// </summary>
    public static class GloveThrowMath
    {
        /// <summary>
        /// Local offset from the right-hand equipment / glove model into the palm.
        /// Used when renderer bounds are unavailable.
        /// </summary>
        public static readonly Vector3 HeldBallHandLocalOffset = new Vector3(0.05f, 0.02f, 0.2f);

        /// <summary>
        /// Fallback body-relative offset when no hand equipment transform exists.
        /// </summary>
        public static readonly Vector3 HeldBallBodyLocalOffset = new Vector3(0.35f, 1.15f, 0.45f);

        /// <summary>
        /// Nudge from the glove mesh bounds center along the hand forward/up so the
        /// ball sits in the pocket rather than inside the mesh.
        /// </summary>
        public const float HeldBallPalmForward = 0.07f;
        public const float HeldBallPalmUp = 0.02f;

        /// <summary>Upward bias used for knockout fling when not otherwise configured.</summary>
        public const float KnockoutUpwardBias = 0.45f;

        public static Vector3 ComputeThrowVelocity(
            Vector3 aimDirection,
            float charge01,
            float minimumSpeed,
            float maximumSpeed,
            float upwardBias
        )
        {
            float t = Mathf.Clamp01(charge01);
            float speed = Mathf.Lerp(minimumSpeed, maximumSpeed, t);
            if (aimDirection.sqrMagnitude < 0.0001f)
                aimDirection = Vector3.forward;
            Vector3 dir = (aimDirection.normalized + Vector3.up * upwardBias).normalized;
            return dir * speed;
        }

        public static Vector3 ComputeKnockoutEjectVelocity(float speed, float upwardBias)
        {
            float yaw = Random.Range(0f, Mathf.PI * 2f);
            Vector3 horizontal = new Vector3(Mathf.Cos(yaw), 0f, Mathf.Sin(yaw));
            Vector3 dir = (horizontal + Vector3.up * upwardBias).normalized;
            return dir * speed;
        }

        /// <summary>
        /// Backspin / tumble for a thrown ball. Matches the lob-item pattern
        /// (axis ⊥ to flight × up, magnitude from speed).
        /// </summary>
        public static Vector3 ComputeThrowAngularVelocity(Vector3 linearVelocity)
        {
            float speed = linearVelocity.magnitude;
            if (speed < 0.01f)
                return Vector3.zero;

            Vector3 spinAxis = Vector3.Cross(linearVelocity.normalized, Vector3.up);
            if (spinAxis.sqrMagnitude < 0.01f)
                spinAxis = Vector3.right;
            else
                spinAxis.Normalize();

            // ~rad/s — scales with throw power so soft tosses tumble less.
            return spinAxis * Mathf.Lerp(6f, 14f, Mathf.Clamp01(speed / 25f));
        }

        /// <summary>
        /// World position for the held ball, preferring the glove / right-hand
        /// equipment so it tracks the hand pose instead of floating at chest height.
        /// </summary>
        /// <param name="info">Holding player.</param>
        /// <param name="gloveModel">Optional custom held-model root (glove mesh).</param>
        public static Vector3 GetHeldWorldPosition(PlayerInfo info, Transform gloveModel = null)
        {
            Transform hand = info?.RightHandEquipmentSwitcher?.transform;

            if (gloveModel != null)
            {
                var renderer = gloveModel.GetComponentInChildren<Renderer>();
                if (renderer != null)
                {
                    Vector3 forward = hand != null ? hand.forward : gloveModel.forward;
                    Vector3 up = hand != null ? hand.up : gloveModel.up;
                    return renderer.bounds.center
                        + forward * HeldBallPalmForward
                        + up * HeldBallPalmUp;
                }

                return gloveModel.TransformPoint(HeldBallHandLocalOffset);
            }

            if (hand != null)
                return hand.TransformPoint(HeldBallHandLocalOffset);

            // Last resort: body-relative (no hand switcher yet / edge cases).
            if (info?.Rigidbody != null)
            {
                var body = info.Rigidbody;
                return body.position + body.rotation * HeldBallBodyLocalOffset;
            }

            if (info != null)
                return info.transform.TransformPoint(HeldBallBodyLocalOffset);

            return Vector3.zero;
        }
    }
}
