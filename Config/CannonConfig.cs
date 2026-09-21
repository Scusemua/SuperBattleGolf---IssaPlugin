using BepInEx.Configuration;
using UnityEngine.InputSystem;

namespace IssaPlugin
{
    public class CannonConfig
    {
        private const string Section = "Cannon";

        public ConfigEntry<Key> GiveKey { get; private set; }

        /// <summary>Number of carts per Golf Cart Launcher pickup. Each shot consumes one use.</summary>
        public ConfigEntry<float> Uses { get; private set; }

        /// <summary>Seconds between successive shots while the fire button is held.</summary>
        public ConfigEntry<float> FireRate { get; private set; }

        /// <summary>Speed (m/s) the launched cart is given along the aim direction.</summary>
        public ConfigEntry<float> LaunchSpeed { get; private set; }

        public ConfigEntry<float> BowlingBallMass { get; private set; }

        public ConfigEntry<float> BowlingBallMassMultiplier { get; private set; }

        /// <summary>
        /// Seconds a launched bowling ball survives before the server despawns it.
        /// Prevents launched bowling balls from piling up. 0 disables the timeout.
        /// </summary>
        public ConfigEntry<float> BowlingBallLifetime { get; private set; }

        /// <summary>Intensity of the screen shake applied when firing. 0 disables it.</summary>
        public ConfigEntry<float> ScreenShakeIntensity { get; private set; }

        public CannonConfig(ConfigFile cfg, GlobalConfig global)
        {
            GiveKey = cfg.Bind(
                Section,
                "GiveKey",
                Key.None,
                "Hotkey to give yourself a Cannon (debug/testing)."
            );

            Uses = cfg.Bind(
                Section,
                "Uses",
                3f,
                new ConfigDescription(
                    "Number of bowling balls per pickup.",
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
                    "Speed (metres/second) the launched bowling ball is given along the aim direction.",
                    new AcceptableValueRange<float>(1f, 300f)
                )
            );

            BowlingBallLifetime = cfg.Bind(
                Section,
                "Lifetime",
                300f,
                new ConfigDescription(
                    "Seconds a launched bowling ball survives before the server despawns it. 0 disables the timeout (carts persist until the hole ends).",
                    new AcceptableValueRange<float>(0f, 600f)
                )
            );

            ScreenShakeIntensity = cfg.Bind(
                Section,
                "ScreenShakeIntensity",
                0.425f,
                new ConfigDescription(
                    "Intensity of the screen shake when firing the Cannon. 0 disables it.",
                    new AcceptableValueRange<float>(0f, 5f)
                )
            );

            BowlingBallMass = cfg.Bind(
                Section,
                "BowlingBallMass",
                40f,
                new ConfigDescription(
                    "Mass of the bowling balls. Cart/prop shove comes only from Unity collision; raise this for harder hits, lower for softer.",
                    new AcceptableValueRange<float>(0.1f, 2000f)
                )
            );

            BowlingBallMassMultiplier = cfg.Bind(
                Section,
                "BowlingBallMassMultiplier",
                1.0f,
                new ConfigDescription(
                    "MUltiplies the configured mass of the bowling balls.",
                    new AcceptableValueRange<float>(1f, 50f)
                )
            );
        }
    }
}
