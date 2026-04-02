using BepInEx.Configuration;
using UnityEngine.InputSystem;

namespace IssaPlugin
{
    public class BaseballBatConfig
    {
        private const string Section = "BaseballBat";

        public ConfigEntry<float> PowerMultiplier { get; private set; }
        public ConfigEntry<float> Uses { get; private set; }
        public ConfigEntry<Key> GiveKey { get; private set; }
        public ConfigEntry<float> SpawnWeight { get; private set; }

        public BaseballBatConfig(ConfigFile cfg, GlobalConfig global)
        {
            PowerMultiplier = cfg.Bind(
                Section,
                "PowerMultiplier",
                3.0f,
                "Multiplier applied to the golf swing power when using the bat."
            );

            Uses = cfg.Bind(
                Section,
                "Uses",
                99.0f,
                "Number of swings before the bat is consumed. Set high for near-infinite use."
            );

            GiveKey = cfg.Bind(
                Section,
                "GiveKey",
                Key.F7,
                "Key to press to add the baseball bat to your inventory."
            );

            SpawnWeight = global.BindSpawnWeight(cfg, 100, "BaseballBatWeight", 15f, "Override spawn weight for the Baseball Bat.");
        }
    }
}
