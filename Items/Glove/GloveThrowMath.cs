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
        /// Code-default hand offset relative to the player's transform while holding.
        /// Tunable later via config if needed.
        /// </summary>
        public static readonly Vector3 HeldBallLocalOffset = new Vector3(0.35f, 1.15f, 0.45f);

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

        public static Vector3 GetHeldWorldPosition(Transform holder)
        {
            return GetHeldWorldPosition(holder, holderBody: null);
        }

        /// <summary>
        /// Prefer the holder's Rigidbody pose when available so the ball follows
        /// teleports / warps that update the body before the Transform catches up.
        /// </summary>
        public static Vector3 GetHeldWorldPosition(Transform holder, Rigidbody holderBody)
        {
            if (holderBody != null)
                return holderBody.position + holderBody.rotation * HeldBallLocalOffset;
            return holder.TransformPoint(HeldBallLocalOffset);
        }
    }
}
