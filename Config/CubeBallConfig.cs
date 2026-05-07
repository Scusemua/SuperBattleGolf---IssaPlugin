using BepInEx.Configuration;
using UnityEngine.InputSystem;

namespace IssaPlugin
{
    public class CubeBallConfig
    {
        private const string Section = "CubeBall";

        public ConfigEntry<Key> GiveKey { get; private set; }
        public ConfigEntry<float> Uses { get; private set; }
        public ConfigEntry<float> Duration { get; private set; }
        public ConfigEntry<float> SpawnWeight { get; private set; }
        public ConfigEntry<float> PhysicsStaticFriction { get; private set; }
        public ConfigEntry<float> PhysicsDynamicFriction { get; private set; }
        public ConfigEntry<float> PhysicsBounciness { get; private set; }

        public CubeBallConfig(ConfigFile cfg, GlobalConfig global)
        {
            GiveKey = cfg.Bind(
                Section,
                "GiveKey",
                Key.None,
                "Hotkey to give yourself a Cube Ball (debug/testing)."
            );
            Uses = cfg.Bind(Section, "Uses", 1f, "Number of uses per Cube Ball pickup.");
            Duration = cfg.Bind(
                Section,
                "Duration",
                10f,
                "Seconds the target player's golf ball remains a cube."
            );
            SpawnWeight = cfg.Bind(
                Section,
                "SpawnWeight",
                5f,
                "Relative spawn weight for the Cube Ball item in the item pool."
            );
            PhysicsStaticFriction = cfg.Bind(
                Section,
                "PhysicsStaticFriction",
                0.8f,
                "Static friction of the cube's physics material. Higher = more grip, more tumbling."
            );
            PhysicsDynamicFriction = cfg.Bind(
                Section,
                "PhysicsDynamicFriction",
                0.8f,
                "Dynamic friction of the cube's physics material. Higher = more resistance while sliding."
            );
            PhysicsBounciness = cfg.Bind(
                Section,
                "PhysicsBounciness",
                0.1f,
                new ConfigDescription(
                    "Bounciness of the cube's physics material (0 = no bounce, 1 = full bounce).",
                    new AcceptableValueRange<float>(0.0f, 1.0f)
                )
            );
        }
    }
}
