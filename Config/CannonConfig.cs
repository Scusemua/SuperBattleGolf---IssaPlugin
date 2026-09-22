using BepInEx.Configuration;
using UnityEngine.InputSystem;

namespace IssaPlugin
{
    public class CannonConfig
    {
        private const string Section = "Cannon";

        public ConfigEntry<Key> GiveKey { get; private set; }

        /// <summary>
        /// Hotkey that asks the server to launch a bowling ball at the local player
        /// from TestFireDistance away (debug/testing). Key.None disables it.
        /// </summary>
        public ConfigEntry<Key> TestFireAtSelfKey { get; private set; }

        /// <summary>Number of carts per Golf Cart Launcher pickup. Each shot consumes one use.</summary>
        public ConfigEntry<float> Uses { get; private set; }

        /// <summary>Seconds between successive shots while the fire button is held.</summary>
        public ConfigEntry<float> FireRate { get; private set; }

        /// <summary>Speed (m/s) the launched cart is given along the aim direction.</summary>
        public ConfigEntry<float> LaunchSpeed { get; private set; }

        public ConfigEntry<float> BowlingBallMass { get; private set; }

        public ConfigEntry<float> BowlingBallMassMultiplier { get; private set; }

        /// <summary>
        /// Fraction of the ball's velocity applied as ForceMode.VelocityChange when a
        /// player is hit (host and remote). 0.15 ≈ 8 m/s at the default 55 launch speed.
        /// </summary>
        public ConfigEntry<float> ClientHitVelocityScale { get; private set; }

        /// <summary>
        /// Hard cap (m/s) on the scripted hit VelocityChange after scaling. 0 disables.
        /// </summary>
        public ConfigEntry<float> ClientHitMaxSpeed { get; private set; }

        /// <summary>
        /// Seconds after launch during which the ball ignores the shooter's colliders.
        /// </summary>
        public ConfigEntry<float> ThrowerIgnoreDuration { get; private set; }

        /// <summary>
        /// How far in front of the local player a TestFireAtSelfKey ball spawns.
        /// </summary>
        public ConfigEntry<float> TestFireDistance { get; private set; }

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

            TestFireAtSelfKey = cfg.Bind(
                Section,
                "TestFireAtSelfKey",
                Key.None,
                "Hotkey to launch a bowling ball at yourself from TestFireDistance away (debug/testing). Key.None disables."
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

            ThrowerIgnoreDuration = cfg.Bind(
                Section,
                "ThrowerIgnoreDuration",
                2.0f,
                new ConfigDescription(
                    "Seconds after launch during which the ball ignores the shooter's colliders. 0 disables the grace period.",
                    new AcceptableValueRange<float>(0f, 10f)
                )
            );

            TestFireDistance = cfg.Bind(
                Section,
                "TestFireDistance",
                7.5f,
                new ConfigDescription(
                    "Distance (metres) in front of the local player at which a TestFireAtSelfKey ball spawns.",
                    new AcceptableValueRange<float>(5f, 10f)
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
                    "Multiplies the configured mass of the bowling balls.",
                    new AcceptableValueRange<float>(1f, 100f)
                )
            );

            ClientHitVelocityScale = cfg.Bind(
                Section,
                "ClientHitVelocityScale",
                0.15f,
                new ConfigDescription(
                    "Fraction of ball velocity applied as VelocityChange on hit (host and remote). Lower if players fly too far; raise if they barely move.",
                    new AcceptableValueRange<float>(0f, 5f)
                )
            );

            ClientHitMaxSpeed = cfg.Bind(
                Section,
                "ClientHitMaxSpeed",
                18f,
                new ConfigDescription(
                    "Hard cap (m/s) on the scripted hit VelocityChange after scaling. 0 disables the cap.",
                    new AcceptableValueRange<float>(0f, 500f)
                )
            );
        }
    }
}
