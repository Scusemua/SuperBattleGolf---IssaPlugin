using BepInEx.Configuration;
using UnityEngine.InputSystem;

namespace IssaPlugin
{
    public class RedBullConfig
    {
        private const string Section = "RedBull";

        public ConfigEntry<Key> GiveKey { get; private set; }
        public ConfigEntry<float> Uses { get; private set; }
        public ConfigEntry<float> SpawnWeight { get; private set; }
        public ConfigEntry<float> Duration { get; private set; }
        public ConfigEntry<float> ExtraSpeedMultiplier { get; private set; }
        public ConfigEntry<float> JumpBonus { get; private set; }
        public ConfigEntry<float> DispenserChance { get; private set; }

        public RedBullConfig(ConfigFile cfg, GlobalConfig global)
        {
            GiveKey = cfg.Bind(
                Section,
                "GiveKey",
                Key.NumpadMultiply,
                "Hotkey to give yourself a Red Bull (debug/testing)."
            );
            Uses = cfg.Bind(Section, "Uses", 1f, "Number of uses per Red Bull item.");
            Duration = cfg.Bind(
                Section,
                "Duration",
                8f,
                "How long the Red Bull speed and jump boost lasts (seconds)."
            );
            ExtraSpeedMultiplier = cfg.Bind(
                Section,
                "ExtraSpeedMultiplier",
                1.5f,
                "Additional speed multiplier applied on top of the coffee speed-boost factor while Red Bull is active."
            );
            JumpBonus = cfg.Bind(
                Section,
                "JumpBonus",
                3f,
                "Extra upward velocity (m/s) added to each jump while Red Bull is active."
            );
            DispenserChance = cfg.Bind(
                Section,
                "DispenserChance",
                0.25f,
                "Probability (0–1) that the coffee dispenser gives a Red Bull instead of a coffee. 0 = never, 1 = always."
            );

            SpawnWeight = global.BindSpawnWeight(
                cfg,
                119,
                "RedBullSpawnWeight",
                10f,
                "Override spawn weight for Red Bull."
            );
        }
    }
}
