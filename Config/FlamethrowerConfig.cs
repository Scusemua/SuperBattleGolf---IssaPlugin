using BepInEx.Configuration;
using UnityEngine.InputSystem;

namespace IssaPlugin
{
    public class FlamethrowerConfig
    {
        private const string Section = "Flamethrower";

        public ConfigEntry<Key> GiveKey { get; private set; }

        /// <summary>Number of fuel canisters per Flamethrower pickup.</summary>
        public ConfigEntry<float> Uses { get; private set; }

        /// <summary>Seconds of fire provided by each fuel canister.</summary>
        public ConfigEntry<float> FuelPerUse { get; private set; }

        /// <summary>
        /// How long (seconds) a hit player stays on fire after the last
        /// flamethrower contact. Each new hit while already burning resets the timer.
        /// </summary>
        public ConfigEntry<float> BurnDuration { get; private set; }

        /// <summary>
        /// Minimum seconds between successive burn-request messages sent per victim.
        /// Prevents spamming the server every physics tick while the flame collider
        /// overlaps a player.
        /// </summary>
        public ConfigEntry<float> BurnRequestInterval { get; private set; }

        public FlamethrowerConfig(ConfigFile cfg, GlobalConfig global)
        {
            GiveKey = cfg.Bind(
                Section,
                "GiveKey",
                Key.F5,
                "Hotkey to give yourself a Flamethrower (debug/testing)."
            );

            Uses = cfg.Bind(
                Section,
                "Uses",
                1f,
                "Number of fuel canisters per Flamethrower pickup. Each canister provides FuelPerUse seconds of fire."
            );

            FuelPerUse = cfg.Bind(
                Section,
                "FuelPerUse",
                3f,
                "Seconds of continuous fire provided by each fuel canister."
            );

            BurnDuration = cfg.Bind(
                Section,
                "BurnDuration",
                4f,
                "Seconds a hit player stays on fire after the last flamethrower contact. Resets on each new hit."
            );

            BurnRequestInterval = cfg.Bind(
                Section,
                "BurnRequestInterval",
                0.5f,
                "Minimum seconds between burn-request messages sent per victim while the flame collider overlaps them."
            );
        }
    }
}
