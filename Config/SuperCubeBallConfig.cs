using BepInEx.Configuration;
using UnityEngine.InputSystem;

namespace IssaPlugin
{
    public class SuperCubeBallConfig
    {
        private const string Section = "SuperCubeBall";

        public ConfigEntry<Key> GiveKey { get; private set; }
        public ConfigEntry<float> Uses { get; private set; }
        public ConfigEntry<float> Duration { get; private set; }
        public ConfigEntry<float> SpawnWeight { get; private set; }
        public ConfigEntry<bool> AffectSelf { get; private set; }

        public SuperCubeBallConfig(ConfigFile cfg, GlobalConfig global)
        {
            GiveKey = cfg.Bind(
                Section,
                "GiveKey",
                Key.None,
                "Hotkey to give yourself a Super Cube Ball (debug/testing)."
            );
            Uses = cfg.Bind(Section, "Uses", 1f, "Number of uses per Super Cube Ball pickup.");
            Duration = cfg.Bind(
                Section,
                "Duration",
                20f,
                "Seconds all other players' golf balls remain cubes."
            );
            SpawnWeight = cfg.Bind(
                Section,
                "SpawnWeight",
                3f,
                "Relative spawn weight for the Super Cube Ball item in the item pool."
            );
            AffectSelf = cfg.Bind(
                Section,
                "AffectSelf",
                false,
                "If true, the activating player's own ball is also cubed."
            );
        }
    }
}
