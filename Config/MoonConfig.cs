using BepInEx.Configuration;
using UnityEngine.InputSystem;

namespace IssaPlugin
{
    public class MoonConfig
    {
        private const string Section = "Moon";

        public ConfigEntry<Key> GiveKey { get; private set; }
        public ConfigEntry<float> Uses { get; private set; }

        // Approach
        public ConfigEntry<float> ApproachDuration { get; private set; }
        public ConfigEntry<float> SpawnDistance { get; private set; }
        public ConfigEntry<float> SpawnHeight { get; private set; }

        // Impact
        public ConfigEntry<float> ImpactHeight { get; private set; }

        // Suck phase
        public ConfigEntry<float> SuckDuration { get; private set; }
        public ConfigEntry<float> PullForce { get; private set; }
        public ConfigEntry<float> MaxPullSpeed { get; private set; }
        public ConfigEntry<float> PullRadius { get; private set; }
        public ConfigEntry<bool> PullAffectsGolfBalls { get; private set; }
        public ConfigEntry<bool> PullAffectsWielder { get; private set; }

        // Explosion
        public ConfigEntry<float> ExplosionForce { get; private set; }
        public ConfigEntry<float> ExplosionRadius { get; private set; }
        public ConfigEntry<float> InitialScale { get; private set; }

        public ConfigEntry<float> FinalScale { get; private set; }

        public MoonConfig(ConfigFile cfg, GlobalConfig global)
        {
            GiveKey = cfg.Bind(
                Section,
                "GiveKey",
                Key.None,
                "Hotkey to give yourself a Majora's Moon (debug/testing)."
            );
            Uses = cfg.Bind(Section, "Uses", 1f, "Number of uses per Moon pickup.");
            ApproachDuration = cfg.Bind(
                Section,
                "ApproachDuration",
                60f,
                "Seconds for the moon to travel from spawn to above the course."
            );
            SpawnDistance = cfg.Bind(
                Section,
                "SpawnDistance",
                999f,
                "Horizontal distance (units) from the hole at which the moon spawns."
            );
            SpawnHeight = cfg.Bind(
                Section,
                "SpawnHeight",
                999f,
                "Height (units) above ground at which the moon spawns."
            );
            ImpactHeight = cfg.Bind(
                Section,
                "ImpactHeight",
                350f,
                "Height (units) above the hole where the moon stops before pulling players in."
            );
            SuckDuration = cfg.Bind(
                Section,
                "SuckDuration",
                8f,
                "Seconds the moon hovers at impact height while pulling all players upward."
            );
            PullForce = cfg.Bind(
                Section,
                "PullForce",
                550f,
                "Peak upward pull force (m/s²) applied to the local player during the suck phase."
            );
            MaxPullSpeed = cfg.Bind(
                Section,
                "MaxPullSpeed",
                999f,
                "Maximum velocity (m/s) toward the moon allowed during the suck phase."
            );
            PullRadius = cfg.Bind(
                Section,
                "PullRadius",
                999f,
                "Distance (units) at which pull force begins to fall off quadratically."
            );
            ExplosionForce = cfg.Bind(
                Section,
                "ExplosionForce",
                999f,
                "Peak velocity change (m/s) applied to non-player Rigidbodies at the moon's impact point."
            );
            ExplosionRadius = cfg.Bind(
                Section,
                "ExplosionRadius",
                250f,
                "Radius (units) within which the explosion force affects nearby Rigidbodies."
            );
            PullAffectsGolfBalls = cfg.Bind(
                Section,
                "PullAffectsGolfBalls",
                false,
                "If true, the moon's suck phase pulls nearby golf balls toward it."
            );
            PullAffectsWielder = cfg.Bind(
                Section,
                "PullAffectsWielder",
                false,
                "If true, the player who used the Moon item is also pulled toward it."
            );
            InitialScale = cfg.Bind(Section, "InitialScale", 0.01f, "Initial scale of the moon.");
            FinalScale = cfg.Bind(Section, "FinalScale", 3f, "Final scale of the moon.");
        }
    }
}
