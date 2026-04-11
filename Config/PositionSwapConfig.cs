using BepInEx.Configuration;
using UnityEngine.InputSystem;

namespace IssaPlugin
{
    public class PositionSwapConfig
    {
        private const string Section = "PositionSwap";

        public ConfigEntry<Key> GiveKey { get; private set; }
        public ConfigEntry<float> Uses { get; private set; }
        public ConfigEntry<float> Delay { get; private set; }

        public PositionSwapConfig(ConfigFile cfg, GlobalConfig global)
        {
            GiveKey = cfg.Bind(
                Section,
                "GiveKey",
                Key.NumpadMinus,
                "Debug key to add the Position Swap item to your inventory."
            );
            Uses = cfg.Bind(Section, "Uses", 1f, "Number of uses per Position Swap pickup.");
            Delay = cfg.Bind(
                Section,
                "Delay",
                3f,
                "Seconds between selecting a swap target and the swap executing. During this time a warning orb appears under both players."
            );
        }
    }
}
