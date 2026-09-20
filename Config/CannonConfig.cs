using BepInEx.Configuration;
using UnityEngine.InputSystem;

namespace IssaPlugin
{
    public class CannonConfig
    {
        private const string Section = "Cannon";

        /// <summary>Number of carts per Golf Cart Launcher pickup. Each shot consumes one use.</summary>
        public ConfigEntry<float> Uses { get; private set; }

        /// <summary>Seconds between successive shots while the fire button is held.</summary>
        public ConfigEntry<float> FireRate { get; private set; }

        /// <summary>Speed (m/s) the launched cart is given along the aim direction.</summary>
        public ConfigEntry<float> LaunchSpeed { get; private set; }

        public CannonConfig(ConfigFile cfg, GlobalConfig global)
        {
            Uses = cfg.Bind(
                Section,
                "Uses",
                3f,
                new ConfigDescription(
                    "Number of cannon balls per pickup.",
                    new AcceptableValueRange<float>(1f, 50f)
                )
            );

            FireRate = cfg.Bind(
                Section,
                "FireRate",
                0.75f,
                new ConfigDescription(
                    "Seconds between successive shots while the fire button is held.",
                    new AcceptableValueRange<float>(0.05f, 10f)
                )
            );

            LaunchSpeed = cfg.Bind(
                Section,
                "LaunchSpeed",
                55f,
                new ConfigDescription(
                    "Speed (metres/second) the launched cannon ball is given along the aim direction.",
                    new AcceptableValueRange<float>(1f, 300f)
                )
            );
        }
    }
}
