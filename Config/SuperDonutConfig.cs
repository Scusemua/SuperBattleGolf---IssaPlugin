using BepInEx.Configuration;
using UnityEngine.InputSystem;

namespace IssaPlugin
{
    public class SuperDonutConfig
    {
        private const string Section = "SuperDonut";

        public ConfigEntry<Key> GiveKey { get; private set; }
        public ConfigEntry<float> Uses { get; private set; }
        public ConfigEntry<float> SpawnWeight { get; private set; }

        public SuperDonutConfig(ConfigFile cfg, GlobalConfig global)
        {
            GiveKey = cfg.Bind(
                Section,
                "GiveKey",
                Key.NumpadDivide,
                "Hotkey to give yourself a Super Donut (debug/testing)."
            );
            Uses = cfg.Bind(Section, "Uses", 1f, "Number of Super Donut uses per pickup.");

            SpawnWeight = global.BindSpawnWeight(
                cfg,
                120,
                "SuperDonutSpawnWeight",
                3f,
                "Override spawn weight for the Super Donut. Keep low — fires at every other player at once."
            );
        }
    }
}
