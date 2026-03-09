using System.Collections.Generic;
using UnityEngine;

namespace IssaPlugin.Items
{
    /// Tracks which rockets belong to custom items and their explosion scale.
    /// Unity is single-threaded, so ActiveScale is safe to use as a "current context" value
    /// that is set in ServerExplode Prefix and read by downstream patches.
    public static class ExplosionScaler
    {
        private static readonly Dictionary<Rocket, float> _scales = new();

        /// Set by ServerExplode Prefix, read by VFX and knockback patches.
        /// Reset to 1 in ServerExplode Postfix.
        public static float ActiveScale { get; set; } = 1f;

        public static void Register(Rocket rocket, float scale)
        {
            if (rocket != null)
                _scales[rocket] = scale;
        }

        public static void Unregister(Rocket rocket)
        {
            if (rocket != null)
                _scales.Remove(rocket);
        }

        public static float GetScale(Rocket rocket)
        {
            if (rocket != null && _scales.TryGetValue(rocket, out float scale))
                return scale;
            return 1f;
        }
    }
}
