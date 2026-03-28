using System.Collections.Generic;

namespace IssaPlugin.Items
{
    /// Tracks which rockets belong to custom items and their explosion scale.
    ///
    /// Keyed by instance ID (int) rather than the Rocket component reference.
    /// This is necessary because Harmony's Postfix on Rocket.ServerExplode runs
    /// AFTER DestroyEntity() has been called inside that method.  Unity's
    /// operator== returns true for destroyed objects ("fake null"), so a
    /// Dictionary<Rocket, float> lookup would always miss.  Instance IDs are
    /// stable for the lifetime of the managed C# wrapper (which outlives the
    /// native destroy within the same frame), so they remain valid in the Postfix.
    /// ReferenceEquals is used instead of == throughout to bypass Unity's fake-null.
    public static class ExplosionScaler
    {
        private static readonly Dictionary<int, float> _scales = new();

        public static void Register(Rocket rocket, float scale)
        {
            if (!ReferenceEquals(rocket, null))
                _scales[rocket.GetInstanceID()] = scale;
        }

        public static void Unregister(Rocket rocket)
        {
            if (!ReferenceEquals(rocket, null))
                _scales.Remove(rocket.GetInstanceID());
        }

        public static float GetScale(Rocket rocket)
        {
            if (
                !ReferenceEquals(rocket, null)
                && _scales.TryGetValue(rocket.GetInstanceID(), out float scale)
            )
                return scale;
            return 1f;
        }

        /// Returns true if this rocket was spawned by a custom mod item.
        /// Used by RocketPatches to skip standard game rockets.
        public static bool IsRegistered(Rocket rocket) =>
            !ReferenceEquals(rocket, null) && _scales.ContainsKey(rocket.GetInstanceID());
    }
}
