using UnityEngine;

namespace IssaPlugin.Items
{
    /// <summary>
    /// Shared throw velocity math for the Glove.
    ///
    /// Kept free of MonoBehaviour / networking so a future trajectory preview can call
    /// the exact same formula the server uses on release.
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

        public static Vector3 GetHeldWorldPosition(Transform holder)
        {
            return holder.TransformPoint(HeldBallLocalOffset);
        }
    }
}
