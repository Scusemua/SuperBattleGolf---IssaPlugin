using BepInEx.Configuration;
using UnityEngine.InputSystem;

namespace IssaPlugin
{
    public class BaseballBatConfig
    {
        private const string Section = "BaseballBat";

        public ConfigEntry<float> PowerMultiplier { get; private set; }
        public ConfigEntry<float> GolfBallPowerMultiplier { get; private set; }
        public ConfigEntry<float> Uses { get; private set; }
        public ConfigEntry<Key> GiveKey { get; private set; }

        public BaseballBatConfig(ConfigFile cfg, GlobalConfig global)
        {
            PowerMultiplier = cfg.Bind(
                Section,
                "PowerMultiplier",
                2.5f,
                "Multiplier applied to swing power for non-golf-ball hits (e.g. golf carts)."
            );

            GolfBallPowerMultiplier = cfg.Bind(
                Section,
                "GolfBallPowerMultiplier",
                1.25f,
                "Multiplier applied to swing power specifically for golf ball hits."
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
        }
    }
}
