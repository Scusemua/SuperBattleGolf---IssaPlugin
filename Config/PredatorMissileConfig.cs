using BepInEx.Configuration;
using UnityEngine.InputSystem;

namespace IssaPlugin
{
    public class PredatorMissileConfig
    {
        private const string Section = "PredatorMissile";

        public ConfigEntry<float> Altitude { get; private set; }
        public ConfigEntry<float> FallSpeed { get; private set; }
        public ConfigEntry<float> SteerSpeed { get; private set; }
        public ConfigEntry<float> Timeout { get; private set; }
        public ConfigEntry<float> Uses { get; private set; }
        public ConfigEntry<Key> GiveKey { get; private set; }
        public ConfigEntry<float> SpawnWeight { get; private set; }
        public ConfigEntry<float> ExplosionScale { get; private set; }

        public PredatorMissileConfig(ConfigFile cfg, GlobalConfig global)
        {
            Altitude = cfg.Bind(
                Section,
                "Altitude",
                175f,
                "Height above the player where the missile spawns."
            );
            FallSpeed = cfg.Bind(
                Section,
                "FallSpeed",
                85f,
                "Downward speed of the missile in units per second."
            );
            SteerSpeed = cfg.Bind(
                Section,
                "SteerSpeed",
                35f,
                "Horizontal steering speed when directing the missile."
            );
            Timeout = cfg.Bind(
                Section,
                "Timeout",
                15f,
                "Maximum seconds before the missile auto-detonates."
            );
            Uses = cfg.Bind(Section, "Uses", 1f, "Number of missile uses per pickup.");
            GiveKey = cfg.Bind(
                Section,
                "GiveKey",
                Key.F9,
                "Key to press to add the predator missile to your inventory."
            );

            SpawnWeight = global.BindSpawnWeight(
                cfg,
                102,
                "PredatorMissileWeight",
                10f,
                "Override spawn weight for the Predator Missile."
            );
            ExplosionScale = cfg.Bind(
                "Explosions",
                "PredatorMissileScale",
                3.0f,
                "Multiplier for Predator Missile explosions. Affects blast radius, knockback, and VFX size."
            );
        }
    }
}
