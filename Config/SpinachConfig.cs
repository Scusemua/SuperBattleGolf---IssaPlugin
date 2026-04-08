using BepInEx.Configuration;
using UnityEngine.InputSystem;

namespace IssaPlugin
{
    public class SpinachConfig
    {
        private const string Section = "Spinach";

        public ConfigEntry<Key> GiveKey { get; private set; }
        public ConfigEntry<float> Uses { get; private set; }
        public ConfigEntry<float> SpawnWeight { get; private set; }
        public ConfigEntry<float> PowerMultiplier { get; private set; }
        public ConfigEntry<float> Duration { get; private set; }

        public SpinachConfig(ConfigFile cfg, GlobalConfig global)
        {
            Duration = cfg.Bind(Section, "Duration", 30f, "Duration in seconds of the spinach.");
            PowerMultiplier = cfg.Bind(Section, "PowerMultiplier", 2.5f, "Swing power multiplier.");
            SpawnWeight = global.BindSpawnWeight(
                cfg,
                125,
                "SpinachSpawnWeight",
                10f,
                "Override spawn weight for Spinach."
            );
            Uses = cfg.Bind(Section, "Uses", 1f, "Number of uses");
            GiveKey = cfg.Bind(Section, "GiveKey", Key.F6, "Key to press to get the spinach item.");
        }
    }
}
