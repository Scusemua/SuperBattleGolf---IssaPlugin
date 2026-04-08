using BepInEx.Configuration;
using UnityEngine.InputSystem;

namespace IssaPlugin
{
    public class JavelinConfig
    {
        private const string Section = "Javelin";

        public ConfigEntry<Key> GiveKey { get; private set; }
        public ConfigEntry<float> Uses { get; private set; }
        public ConfigEntry<float> SpawnWeight { get; private set; }
        public ConfigEntry<float> ApexHeight { get; private set; }
        public ConfigEntry<float> AscentSpeed { get; private set; }
        public ConfigEntry<float> DiveSpeed { get; private set; }
        public ConfigEntry<float> DiveAcceleration { get; private set; }
        public ConfigEntry<float> ArrivalRadius { get; private set; }
        public ConfigEntry<float> Timeout { get; private set; }
        public ConfigEntry<float> ExplosionVfxDuration { get; private set; }
        public ConfigEntry<float> ExplosionScale { get; private set; }

        public JavelinConfig(ConfigFile cfg, GlobalConfig global)
        {
            GiveKey = cfg.Bind(
                Section,
                "GiveKey",
                Key.Numpad3,
                "Debug key to add the Javelin to your inventory."
            );
            Uses = cfg.Bind(Section, "Uses", 1f, "Number of Javelin uses per pickup.");
            ApexHeight = cfg.Bind(
                Section,
                "ApexHeight",
                80f,
                "How many units above the launch point the rocket climbs before turning."
            );
            AscentSpeed = cfg.Bind(
                Section,
                "AscentSpeed",
                35f,
                "Speed in units per second during the upward climb phase."
            );
            DiveSpeed = cfg.Bind(
                Section,
                "DiveSpeed",
                55f,
                "Initial speed in units per second when the rocket begins its dive."
            );
            DiveAcceleration = cfg.Bind(
                Section,
                "DiveAcceleration",
                25f,
                "Additional speed gained per second during the dive phase."
            );
            ArrivalRadius = cfg.Bind(
                Section,
                "ArrivalRadius",
                3f,
                "Distance from the target position at which the rocket detonates."
            );
            Timeout = cfg.Bind(
                Section,
                "Timeout",
                20f,
                "Maximum seconds before the rocket force-detonates if it hasn't hit yet."
            );
            ExplosionVfxDuration = cfg.Bind(
                Section,
                "ExplosionVfxDuration",
                5f,
                "Seconds before the Javelin explosion VFX prefab is destroyed on each client."
            );

            SpawnWeight = global.BindSpawnWeight(
                cfg,
                108,
                "JavelinWeight",
                12f,
                "Override spawn weight for the Javelin."
            );
            ExplosionScale = cfg.Bind(
                "Explosions",
                "JavelinScale",
                3.5f,
                "Multiplier for Javelin explosions. Affects blast radius, knockback, and VFX size."
            );
        }
    }
}
