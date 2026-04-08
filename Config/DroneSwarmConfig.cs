using BepInEx.Configuration;
using UnityEngine.InputSystem;

namespace IssaPlugin
{
    public class DroneSwarmConfig
    {
        private const string Section = "DroneSwarm";

        public ConfigEntry<Key> GiveKey { get; private set; }
        public ConfigEntry<float> Uses { get; private set; }
        public ConfigEntry<float> SpawnWeight { get; private set; }
        public ConfigEntry<float> BaseDroneCount { get; private set; }
        public ConfigEntry<float> PerPlayerScalingFactor { get; private set; }
        public ConfigEntry<float> Altitude { get; private set; }
        public ConfigEntry<float> WanderRadius { get; private set; }
        public ConfigEntry<float> WanderSpeed { get; private set; }
        public ConfigEntry<float> WanderTurnRate { get; private set; }
        public ConfigEntry<float> AltitudeVariance { get; private set; }
        public ConfigEntry<float> CircleTimeMin { get; private set; }
        public ConfigEntry<float> CircleTimeMax { get; private set; }
        public ConfigEntry<float> DiveSpeed { get; private set; }
        public ConfigEntry<float> DiveAcceleration { get; private set; }
        public ConfigEntry<float> HomingStopDistance { get; private set; }
        public ConfigEntry<float> ArrivalRadius { get; private set; }
        public ConfigEntry<float> ExplosionScale { get; private set; }
        public ConfigEntry<bool> FriendlyFire { get; private set; }
        public ConfigEntry<bool> AttackFinishedPlayers { get; private set; }
        public ConfigEntry<float> MaxSessionDuration { get; private set; }

        public DroneSwarmConfig(ConfigFile cfg, GlobalConfig global)
        {
            GiveKey = cfg.Bind(
                Section,
                "GiveKey",
                Key.NumpadEnter,
                "Debug key to add the Drone Swarm item to your inventory."
            );
            Uses = cfg.Bind(Section, "Uses", 1f, "Number of uses per Drone Swarm pickup.");
            BaseDroneCount = cfg.Bind(
                Section,
                "BaseDroneCount",
                15f,
                "Number of drones spawned per use. Keep at or below 12 to avoid network overhead."
            );
            PerPlayerScalingFactor = cfg.Bind(
                Section,
                "PerPlayerScalingFactor",
                0.1f,
                new ConfigDescription(
                    "Increase the DroneCount as: DroneCount = BaseDroneCount * (1 + [NumPlayers * PerPlayerScalingFactor]). Set to 0 to disable.",
                    new AcceptableValueRange<float>(0f, 10f)
                )
            );
            Altitude = cfg.Bind(
                Section,
                "Altitude",
                300f,
                "Height (metres) at which drones circle above the player centroid."
            );
            WanderRadius = cfg.Bind(
                Section,
                "WanderRadius",
                150f,
                "Maximum distance (metres) from the wander centre a drone waypoint may be placed."
            );
            WanderSpeed = cfg.Bind(
                Section,
                "WanderSpeed",
                125f,
                "Cruise speed (metres/second) while drones are wandering before they dive."
            );
            WanderTurnRate = cfg.Bind(
                Section,
                "WanderTurnRate",
                45f,
                "Maximum steering rate (degrees/second) while wandering. Lower = lazier, wider arcs. Higher = tighter turns."
            );
            AltitudeVariance = cfg.Bind(
                Section,
                "AltitudeVariance",
                25f,
                "Half-range (metres) of random altitude variation added to each waypoint. Drones pick altitudes between Altitude ± AltitudeVariance."
            );
            CircleTimeMin = cfg.Bind(
                Section,
                "CircleTimeMin",
                3f,
                "Minimum seconds a drone circles before diving (randomised per drone)."
            );
            CircleTimeMax = cfg.Bind(
                Section,
                "CircleTimeMax",
                30f,
                "Maximum seconds a drone circles before diving (randomised per drone)."
            );
            DiveSpeed = cfg.Bind(
                Section,
                "DiveSpeed",
                20f,
                "Initial dive speed in metres per second."
            );
            DiveAcceleration = cfg.Bind(
                Section,
                "DiveAcceleration",
                40f,
                "How many metres per second the dive speed increases each second."
            );
            HomingStopDistance = cfg.Bind(
                Section,
                "HomingStopDistance",
                75f,
                "Distance from the target (metres) at which the drone stops tracking and flies straight, giving the target a chance to dodge. Set to 0 to always home."
            );
            ArrivalRadius = cfg.Bind(
                Section,
                "ArrivalRadius",
                3f,
                "Distance from the aim point (metres) at which the drone detonates."
            );
            ExplosionScale = cfg.Bind(
                Section,
                "ExplosionScale",
                1.05f,
                "Scale multiplier for the drone impact explosion radius and force."
            );
            FriendlyFire = cfg.Bind(
                Section,
                "FriendlyFire",
                false,
                "If true, drones may target the player who summoned them."
            );
            AttackFinishedPlayers = cfg.Bind(
                Section,
                "AttackFinishedPlayers",
                false,
                "If true, drones may target players who have already holed out."
            );
            MaxSessionDuration = cfg.Bind(
                Section,
                "MaxSessionDuration",
                60f,
                "Safety cap (seconds) after which any remaining drones are force-destroyed."
            );

            SpawnWeight = global.BindSpawnWeight(
                cfg,
                118,
                "DroneSwarmSpawnWeight",
                3.5f,
                "Override spawn weight for the Drone Swarm."
            );
        }
    }
}
