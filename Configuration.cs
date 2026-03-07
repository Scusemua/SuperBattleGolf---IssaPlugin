using BepInEx.Configuration;
using UnityEngine.InputSystem;

namespace IssaPlugin
{
    public static class Configuration
    {
        // --- Baseball Bat ---
        public static ConfigEntry<float> BaseballBatPowerMultiplier { get; private set; }
        public static ConfigEntry<int> BaseballBatUses { get; private set; }
        public static ConfigEntry<Key> BaseballBatGiveKey { get; private set; }

        // --- Stealth Bomber ---
        public static ConfigEntry<float> BomberAltitude { get; private set; }
        public static ConfigEntry<float> BomberSpeed { get; private set; }
        public static ConfigEntry<float> BomberRocketInterval { get; private set; }
        public static ConfigEntry<int> BomberRocketCount { get; private set; }
        public static ConfigEntry<float> BomberSpread { get; private set; }
        public static ConfigEntry<int> BomberUses { get; private set; }
        public static ConfigEntry<Key> BomberGiveKey { get; private set; }
        public static ConfigEntry<float> BomberWaitTime { get; private set; }

        public static void Initialize(ConfigFile cfg)
        {
            // Baseball Bat
            BaseballBatPowerMultiplier = cfg.Bind(
                "BaseballBat",
                "PowerMultiplier",
                3.0f,
                "Multiplier applied to the golf swing power when using the bat."
            );

            BaseballBatUses = cfg.Bind(
                "BaseballBat",
                "Uses",
                99,
                "Number of swings before the bat is consumed. Set high for near-infinite use."
            );

            BaseballBatGiveKey = cfg.Bind(
                "BaseballBat",
                "GiveKey",
                Key.F7,
                "Key to press to add the baseball bat to your inventory."
            );

            // Stealth Bomber
            BomberAltitude = cfg.Bind(
                "StealthBomber",
                "Altitude",
                50f,
                "Height above the map the bombing run flies at."
            );

            BomberSpeed = cfg.Bind(
                "StealthBomber",
                "Speed",
                40f,
                "Speed of the bombing run in units per second."
            );

            BomberRocketInterval = cfg.Bind(
                "StealthBomber",
                "RocketInterval",
                0.15f,
                "Seconds between each rocket drop during a bombing run."
            );

            BomberRocketCount = cfg.Bind(
                "StealthBomber",
                "RocketCount",
                12,
                "Total number of rockets dropped per bombing run."
            );

            BomberSpread = cfg.Bind(
                "StealthBomber",
                "Spread",
                5f,
                "Random lateral spread in units for each rocket's drop position."
            );

            BomberUses = cfg.Bind("StealthBomber", "Uses", 1, "Number of bombing runs per pickup.");

            BomberGiveKey = cfg.Bind(
                "StealthBomber",
                "GiveKey",
                Key.F8,
                "Key to press to add the stealth bomber to your inventory."
            );

            BomberWaitTime = cfg.Bind(
                "StealthBomber",
                "WaitTime",
                2.0f,
                "Seconds to wait before starting the bombing run."
            );

            IssaPluginPlugin.Log.LogInfo("Configuration initialized.");
        }
    }
}
