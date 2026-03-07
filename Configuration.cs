using BepInEx.Configuration;

namespace IssaPlugin
{
    public static class Configuration
    {
        // --- Baseball Bat ---
        public static ConfigEntry<float> BaseballBatPower { get; private set; }
        public static ConfigEntry<float> BaseballBatChargeTime { get; private set; }
        public static ConfigEntry<float> BaseballBatCooldown { get; private set; }
        public static ConfigEntry<int> BaseballBatUses { get; private set; }
        public static ConfigEntry<UnityEngine.KeyCode> BaseballBatGiveKey { get; private set; }

        // --- Stealth Bomber ---
        public static ConfigEntry<float> BomberAltitude { get; private set; }
        public static ConfigEntry<float> BomberSpeed { get; private set; }
        public static ConfigEntry<float> BomberRocketInterval { get; private set; }
        public static ConfigEntry<int> BomberRocketCount { get; private set; }
        public static ConfigEntry<float> BomberSpread { get; private set; }
        public static ConfigEntry<int> BomberUses { get; private set; }
        public static ConfigEntry<UnityEngine.KeyCode> BomberGiveKey { get; private set; }

        public static void Initialize(ConfigFile cfg)
        {
            // Baseball Bat
            BaseballBatPower = cfg.Bind("BaseballBat", "Power", 3.0f,
                "Swing power multiplier. Higher values launch targets further.");

            BaseballBatChargeTime = cfg.Bind("BaseballBat", "ChargeTime", 0.1f,
                "Windup delay in seconds before the swing connects.");

            BaseballBatCooldown = cfg.Bind("BaseballBat", "Cooldown", 0.5f,
                "Seconds to wait after a swing before the next swing is allowed.");

            BaseballBatUses = cfg.Bind("BaseballBat", "Uses", 99,
                "Number of swings before the bat is consumed. Set high for near-infinite use.");

            BaseballBatGiveKey = cfg.Bind("BaseballBat", "GiveKey", UnityEngine.KeyCode.F7,
                "Key to press to add the baseball bat to your inventory.");

            // Stealth Bomber
            BomberAltitude = cfg.Bind("StealthBomber", "Altitude", 50f,
                "Height above the map the bombing run flies at.");

            BomberSpeed = cfg.Bind("StealthBomber", "Speed", 40f,
                "Speed of the bombing run in units per second.");

            BomberRocketInterval = cfg.Bind("StealthBomber", "RocketInterval", 0.15f,
                "Seconds between each rocket drop during a bombing run.");

            BomberRocketCount = cfg.Bind("StealthBomber", "RocketCount", 12,
                "Total number of rockets dropped per bombing run.");

            BomberSpread = cfg.Bind("StealthBomber", "Spread", 5f,
                "Random lateral spread in units for each rocket's drop position.");

            BomberUses = cfg.Bind("StealthBomber", "Uses", 1,
                "Number of bombing runs per pickup.");

            BomberGiveKey = cfg.Bind("StealthBomber", "GiveKey", UnityEngine.KeyCode.F8,
                "Key to press to add the stealth bomber to your inventory.");

            IssaPluginPlugin.Log.LogInfo("Configuration initialized.");
        }
    }
}
