using BepInEx.Configuration;
using UnityEngine.InputSystem;

namespace IssaPlugin
{
    public class LowGravityConfig
    {
        private const string Section = "LowGravity";

        public ConfigEntry<Key> GiveKey { get; private set; }
        public ConfigEntry<float> Uses { get; private set; }
        public ConfigEntry<float> Duration { get; private set; }
        public ConfigEntry<float> Scale { get; private set; }

        public LowGravityConfig(ConfigFile cfg, GlobalConfig global)
        {
            GiveKey = cfg.Bind(
                Section,
                "GiveKey",
                Key.Numpad0,
                "Debug key to add the Low Gravity item to your inventory."
            );
            Uses = cfg.Bind(Section, "Uses", 1f, "Number of uses per Low Gravity pickup.");
            Duration = cfg.Bind(
                Section,
                "Duration",
                20f,
                "Seconds the reduced gravity lasts before physics is restored."
            );
            Scale = cfg.Bind(
                Section,
                "GravityScale",
                0.25f,
                "Fraction of normal gravity applied during the effect (e.g. 0.25 = 25%). "
                    + "Affects golf balls (Rigidbody) and player fall/jump height equally, "
                    + "since PlayerMovement reads Physics.gravity directly."
            );
        }
    }
}
