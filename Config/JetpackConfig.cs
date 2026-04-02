using BepInEx.Configuration;
using UnityEngine.InputSystem;

namespace IssaPlugin
{
    public class JetpackConfig
    {
        private const string Section = "Jetpack";

        public ConfigEntry<Key> GiveKey { get; private set; }
        public ConfigEntry<float> Uses { get; private set; }
        public ConfigEntry<float> SpawnWeight { get; private set; }
        public ConfigEntry<float> FuelPerUse { get; private set; }
        public ConfigEntry<float> ThrustForce { get; private set; }

        public JetpackConfig(ConfigFile cfg, GlobalConfig global)
        {
            GiveKey = cfg.Bind(Section, "GiveKey", Key.End, "Hotkey to give yourself a Jetpack (debug/testing).");
            Uses = cfg.Bind(Section, "Uses", 1f, "Number of fuel canisters per Jetpack pickup. Each canister provides JetpackFuelPerUse seconds of thrust.");
            SpawnWeight = global.BindSpawnWeight(cfg, 123, "JetpackSpawnWeight", 10f, "Override spawn weight for the Jetpack.");
            FuelPerUse = cfg.Bind(Section, "FuelPerUse", 1f, "Seconds of thrust provided by each fuel canister. When exhausted, one use is consumed.");
            ThrustForce = cfg.Bind(Section, "ThrustForce", 35f, "Upward acceleration (m/s²) applied each physics step while thrusting. Uses ForceMode.Acceleration.");
        }
    }
}
