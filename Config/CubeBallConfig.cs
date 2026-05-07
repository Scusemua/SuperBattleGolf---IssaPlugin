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
        }
    }
}
