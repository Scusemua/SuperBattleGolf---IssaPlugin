using BepInEx.Configuration;
using UnityEngine.InputSystem;

namespace IssaPlugin
{
    public class GrapplingHookConfig
    {
        private const string Section = "Grappling Hook";

        public ConfigEntry<Key> GiveKey { get; private set; }
        public ConfigEntry<float> Uses { get; private set; }
        public ConfigEntry<float> MaxRange { get; private set; }
        public ConfigEntry<float> MinLength { get; private set; }
        public ConfigEntry<float> ReelSpeed { get; private set; }
        public ConfigEntry<float> SteerAcceleration { get; private set; }
        public ConfigEntry<float> ReleaseGrace { get; private set; }

        public GrapplingHookConfig(ConfigFile cfg, GlobalConfig global)
        {
            GiveKey = cfg.Bind(
                Section,
                "GiveKey",
                Key.None,
                "Debug key to add a Grappling Hook to your inventory."
            );
            Uses = cfg.Bind(
                Section,
                "Uses",
                10f,
                "Successful attaches per pickup. A miss does not consume a use. Re-anchoring consumes another use."
            );
            MaxRange = cfg.Bind(
                Section,
                "MaxRange",
                800f,
                "Longest distance (m) the hook can attach. The rope starts at the distance of the hit."
            );
            MinLength = cfg.Bind(
                Section,
                "MinLength",
                5f,
                "Shortest rope length (m). Further reeling does nothing."
            );
            ReelSpeed = cfg.Bind(
                Section,
                "ReelSpeed",
                30f,
                "Speed (m/s) at which holding fire shortens the rope."
            );
            SteerAcceleration = cfg.Bind(
                Section,
                "SteerAcceleration",
                50f,
                "Acceleration (m/s²) applied perpendicular to the rope while swinging, from movement input."
            );
            ReleaseGrace = cfg.Bind(
                Section,
                "ReleaseGrace",
                0.4f,
                "Seconds after release during which swing speed is kept instead of being bled off by air drag."
            );
        }
    }
}
