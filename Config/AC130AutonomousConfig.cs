using BepInEx.Configuration;

namespace IssaPlugin
{
    /// <summary>
    /// Settings for an AC130 that flies and shoots on its own.
    /// The piloted gunship keeps using <see cref="AC130Config"/>.
    /// Defaults match that config, so an autonomous deploy behaves the same until these are changed.
    /// </summary>
    public class AC130AutonomousConfig
    {
        private const string Section = "AC130 Autonomous";

        public ConfigEntry<bool> TargetsUser { get; private set; }
        public ConfigEntry<float> Duration { get; private set; }
        public ConfigEntry<float> FireCooldown { get; private set; }
        public ConfigEntry<float> HeavyFireCooldown { get; private set; }
        public ConfigEntry<float> HeavyShotsBeforeReload { get; private set; }
        public ConfigEntry<float> HeavyReloadTime { get; private set; }
        public ConfigEntry<float> RocketAngularJitter { get; private set; }
        public ConfigEntry<float> RocketSpeedMultiplier { get; private set; }
        public ConfigEntry<float> AimHeightOffset { get; private set; }
        public ConfigEntry<float> ExplosionScale { get; private set; }
        public ConfigEntry<float> HeavyRocketExplosionScale { get; private set; }
        public ConfigEntry<float> OrbitRadius { get; private set; }
        public ConfigEntry<float> OrbitSpeed { get; private set; }
        public ConfigEntry<float> Altitude { get; private set; }
        public ConfigEntry<float> ApproachDistance { get; private set; }
        public ConfigEntry<float> ApproachSpeed { get; private set; }
        public ConfigEntry<float> FlyOutTime { get; private set; }
        public ConfigEntry<float> HitsToMayday { get; private set; }
        public ConfigEntry<float> CrashTimeout { get; private set; }

        public AC130AutonomousConfig(ConfigFile cfg)
        {
            TargetsUser = cfg.Bind(
                Section,
                "TargetsUser",
                false,
                "Whether an autonomous AC130 may shoot the player who called it. "
                    + "Leave this off in a normal match. Turn it on to test the gunship alone."
            );
            Duration = cfg.Bind(
                Section,
                "Duration",
                25f,
                "How many seconds an autonomous AC130 spends shooting before it leaves."
            );
            FireCooldown = cfg.Bind(
                Section,
                "FireCooldown",
                5f,
                "Minimum seconds between regular rockets from an autonomous AC130."
            );
            HeavyFireCooldown = cfg.Bind(
                Section,
                "HeavyFireCooldown",
                4f,
                "Minimum seconds between heavy rockets from an autonomous AC130."
            );
            HeavyShotsBeforeReload = cfg.Bind(
                Section,
                "HeavyShotsBeforeReload",
                3f,
                "Number of heavy rockets an autonomous AC130 fires before that gun reloads."
            );
            HeavyReloadTime = cfg.Bind(
                Section,
                "HeavyReloadTime",
                8f,
                "Seconds an autonomous AC130 spends reloading the heavy rocket."
            );
            RocketAngularJitter = cfg.Bind(
                Section,
                "RocketAngularJitter",
                0.5f,
                "Random angular jitter in degrees applied to each rocket from an autonomous AC130."
            );
            RocketSpeedMultiplier = cfg.Bind(
                Section,
                "RocketSpeedMultiplier",
                1.75f,
                "Speed multiplier for rockets fired by an autonomous AC130. 1 = the game's default "
                    + "rocket velocity. Does not change rockets fired while piloting."
            );
            AimHeightOffset = cfg.Bind(
                Section,
                "AimHeightOffset",
                1f,
                "Height in units above a player's origin that an autonomous AC130 aims at."
            );
            ExplosionScale = cfg.Bind(
                Section,
                "ExplosionScale",
                2.25f,
                "Explosion scale of regular rockets from an autonomous AC130. Affects blast radius, "
                    + "knockback, and VFX size."
            );
            HeavyRocketExplosionScale = cfg.Bind(
                Section,
                "HeavyRocketExplosionScale",
                5f,
                "Explosion scale of heavy rockets from an autonomous AC130."
            );
            OrbitRadius = cfg.Bind(
                Section,
                "OrbitRadius",
                100f,
                "Radius in units of the circle an autonomous AC130 flies around the map centre."
            );
            OrbitSpeed = cfg.Bind(
                Section,
                "OrbitSpeed",
                6f,
                "Degrees per second at which an autonomous AC130 orbits the map centre."
            );
            Altitude = cfg.Bind(
                Section,
                "Altitude",
                140f,
                "Height above the map centre an autonomous AC130 flies at."
            );
            ApproachDistance = cfg.Bind(
                Section,
                "ApproachDistance",
                200f,
                "How far away an autonomous AC130 spawns and flies in from before reaching its orbit."
            );
            ApproachSpeed = cfg.Bind(
                Section,
                "ApproachSpeed",
                60f,
                "Speed in units per second at which an autonomous AC130 flies in and out."
            );
            FlyOutTime = cfg.Bind(
                Section,
                "FlyOutTime",
                2000f / 60f,
                "Seconds an autonomous AC130 spends flying away after it stops shooting. "
                    + "It keeps moving at ApproachSpeed for this long, then leaves. "
                    + "The default matches the old fly-out: 2000 units at an approach speed of 60."
            );
            HitsToMayday = cfg.Bind(
                Section,
                "HitsToMayday",
                1f,
                "Rocket hits required to shoot down an autonomous AC130. Set to 0 to disable."
            );
            CrashTimeout = cfg.Bind(
                Section,
                "CrashTimeout",
                30f,
                "Seconds an autonomous AC130 is allowed to fall after it is shot down. "
                    + "The gunship is removed when this expires if it has not hit the ground."
            );
        }
    }
}
