using BepInEx.Configuration;
using UnityEngine.InputSystem;

namespace IssaPlugin
{
    public class PowerJammerConfig
    {
        private const string Section = "PowerJammer";

        public ConfigEntry<Key> GiveKey { get; private set; }
        public ConfigEntry<float> Uses { get; private set; }
        public ConfigEntry<float> Duration { get; private set; }
        public ConfigEntry<bool> AffectsUser { get; private set; }

        public PowerJammerConfig(ConfigFile cfg, GlobalConfig global)
        {
            GiveKey = cfg.Bind(
                Section,
                "GiveKey",
                Key.None,
                "Debug key to add the Power Jammer item to your inventory."
            );
            Uses = cfg.Bind(Section, "Uses", 1f, "Number of uses per Power Jammer pickup.");
            Duration = cfg.Bind(
                Section,
                "Duration",
                15f,
                new ConfigDescription(
                    "Seconds that affected players cannot see terrain predictions on the swing power gauge.",
                    new AcceptableValueRange<float>(1f, 120f)
                )
            );
            AffectsUser = cfg.Bind(
                Section,
                "AffectsUser",
                false,
                "Whether the player who activates the Power Jammer is also affected. Defaults to false."
            );
        }
    }
}
