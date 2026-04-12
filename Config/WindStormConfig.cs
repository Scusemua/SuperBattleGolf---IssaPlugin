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
        public ConfigEntry<bool> ExcludeActivator { get; private set; }

        /// How often (seconds) the wind direction shifts during the storm.
        public ConfigEntry<float> DirectionChangeInterval { get; private set; }

        /// Maximum degrees the direction can shift each interval (random walk step).
        /// E.g. 45 means the angle changes by a random amount in [-45, +45] each tick.
        public ConfigEntry<float> DirectionChangeRange { get; private set; }

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
                    + "The base game High preset maxes out at 99 km/h."
            );
            ExcludeActivator = cfg.Bind(
                Section,
                "ExcludeActivator",
                true,
                "When true, the player who activates the storm is immune — their ball is "
                    + "unaffected by the storm wind. When false, everyone including the "
                    + "activator is hit by the storm."
            );
            DirectionChangeInterval = cfg.Bind(
                Section,
                "DirectionChangeInterval",
                2f,
                "Seconds between each wind direction shift during the storm. "
                    + "Smaller values produce more turbulent, rapidly-changing gusts."
            );
            DirectionChangeRange = cfg.Bind(
                Section,
                "DirectionChangeRange",
                45f,
                "Maximum degrees the wind direction can shift each interval (random walk). "
                    + "E.g. 45 means the angle changes by a random amount in [-45, +45] per tick."
            );
        }
    }
}
