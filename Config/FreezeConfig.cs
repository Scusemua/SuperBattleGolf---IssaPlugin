using BepInEx.Configuration;
using UnityEngine.InputSystem;

namespace IssaPlugin
{
    public class FreezeConfig
    {
        private const string Section = "FreezeWorld";

        public ConfigEntry<Key> GiveKey { get; private set; }
        public ConfigEntry<float> Uses { get; private set; }
        public ConfigEntry<float> Duration { get; private set; }
        public ConfigEntry<float> Friction { get; private set; }
        public ConfigEntry<float> Bounciness { get; private set; }
        public ConfigEntry<float> CartSidewaysStiffness { get; private set; }
        public ConfigEntry<float> GripRadius { get; private set; }

        public FreezeConfig(ConfigFile cfg, GlobalConfig global)
        {
            GiveKey = cfg.Bind(
                Section,
                "GiveKey",
                Key.F12,
                "Debug key to add the Freeze World item to your inventory."
            );
            Uses = cfg.Bind(Section, "Uses", 1f, "Number of uses per Freeze World pickup.");
            Duration = cfg.Bind(
                Section,
                "Duration",
                15f,
                "Seconds the world stays frozen before physics and visuals are restored."
            );
            Friction = cfg.Bind(
                Section,
                "Friction",
                0.02f,
                "Surface friction applied to all physics contacts during a freeze (0 = frictionless)."
            );
            Bounciness = cfg.Bind(
                Section,
                "Bounciness",
                0.2f,
                new ConfigDescription(
                    "Surface bounciness applied to all physics contacts during a freeze.",
                    new AcceptableValueRange<float>(0.0f, 1.0f)
                )
            );
            CartSidewaysStiffness = cfg.Bind(
                Section,
                "CartSidewaysStiffness",
                0.15f,
                "Sideways friction stiffness for golf cart wheel colliders while frozen (0 = no grip, 1 = normal). Lower values cause more drift."
            );
            GripRadius = cfg.Bind(
                Section,
                "GripRadius",
                1.5f,
                "Distance (metres) from the local player's own ball within which normal traction is restored, allowing them to stop and take a shot."
            );
        }
    }
}
