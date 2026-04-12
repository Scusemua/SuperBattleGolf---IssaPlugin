using BepInEx.Configuration;
using UnityEngine.InputSystem;

namespace IssaPlugin
{
    public class WindStormConfig
    {
        private const string Section = "WindStorm";

        public ConfigEntry<Key> GiveKey { get; private set; }
        public ConfigEntry<float> Uses { get; private set; }
        public ConfigEntry<float> Duration { get; private set; }

        /// Wind speed (km/h) applied during the storm.
        /// The base game's "High" preset maxes out at 99 km/h, so anything above that
        /// is genuinely stormy.  Default of 150 is roughly 1.5× hurricane-force.
        public ConfigEntry<float> StormSpeed { get; private set; }

        public WindStormConfig(ConfigFile cfg, GlobalConfig global)
        {
            GiveKey = cfg.Bind(
                Section,
                "GiveKey",
                Key.None,
                "Debug key to add the Wind Storm item to your inventory."
            );
            Uses = cfg.Bind(Section, "Uses", 1f, "Number of uses per Wind Storm pickup.");
            Duration = cfg.Bind(
                Section,
                "Duration",
                15f,
                "Seconds the wind storm lasts before wind returns to normal."
            );
            StormSpeed = cfg.Bind(
                Section,
                "StormSpeed",
                150f,
                "Wind speed (km/h) applied during the storm. "
                    + "The base game High preset maxes out at 99 km/h. "
                    + "The activating player's ball is unaffected by the storm wind."
            );
        }
    }
}
