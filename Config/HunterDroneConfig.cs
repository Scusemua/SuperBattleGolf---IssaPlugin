using BepInEx.Configuration;
using UnityEngine.InputSystem;

namespace IssaPlugin
{
    public class HunterDroneConfig
    {
        private const string Section = "HunterDrone";

        public ConfigEntry<Key> GiveKey { get; private set; }
        public ConfigEntry<float> Uses { get; private set; }
        public ConfigEntry<float> LaunchSpeed { get; private set; }
        public ConfigEntry<float> Acceleration { get; private set; }
        public ConfigEntry<float> HomingStopDistance { get; private set; }
        public ConfigEntry<float> ArrivalRadius { get; private set; }
        public ConfigEntry<float> ExplosionScale { get; private set; }
        public ConfigEntry<float> ArmDelay { get; private set; }
        public ConfigEntry<bool> FriendlyFire { get; private set; }
        public ConfigEntry<bool> AttackFinishedPlayers { get; private set; }
        public ConfigEntry<float> MaxFlightDistance { get; private set; }
        public ConfigEntry<float> MaxSpeed { get; private set; }
        public ConfigEntry<int> MaxActiveDrones { get; private set; }

        public HunterDroneConfig(ConfigFile cfg, GlobalConfig global)
        {
            GiveKey = cfg.Bind(
                Section,
                "GiveKey",
                Key.NumpadPlus,
                "Debug key to add the Hunter Drone item to your inventory."
            );
            Uses = cfg.Bind(Section, "Uses", 1f, "Number of uses per Hunter Drone pickup.");
            LaunchSpeed = cfg.Bind(
                Section,
                "LaunchSpeed",
                90f,
                "Initial flight speed of the drone in metres per second."
            );
            Acceleration = cfg.Bind(
                Section,
                "Acceleration",
                25f,
                "How many metres per second the drone's speed increases each second."
            );
            HomingStopDistance = cfg.Bind(
                Section,
                "HomingStopDistance",
                10f,
                "Distance from the target (metres) at which the drone locks its aim point and "
                    + "flies straight, giving the target a brief chance to dodge. Set to 0 to "
                    + "always track the target."
            );
            ArrivalRadius = cfg.Bind(
                Section,
                "ArrivalRadius",
                2.5f,
                "Distance from the aim point (metres) at which the drone detonates."
            );
            ExplosionScale = cfg.Bind(
                Section,
                "ExplosionScale",
                1.2f,
                "Scale multiplier for the drone impact explosion radius and force."
            );
            ArmDelay = cfg.Bind(
                Section,
                "ArmDelay",
                0.2f,
                "Seconds after launch before the drone starts checking for collision. "
                    + "Prevents the drone from detonating on the thrower immediately after "
                    + "being thrown."
            );
            FriendlyFire = cfg.Bind(
                Section,
                "FriendlyFire",
                false,
                "If true, the drone may target the player who threw it."
            );
            AttackFinishedPlayers = cfg.Bind(
                Section,
                "AttackFinishedPlayers",
                false,
                "If true, the drone may target players who have already holed out."
            );
            MaxFlightDistance = cfg.Bind(
                Section,
                "MaxFlightDistance",
                500f,
                "Maximum distance in metres the drone may travel before self-detonating. "
                    + "Acts as a safety valve when no target exists or the drone misses."
            );
            MaxSpeed = cfg.Bind(
                Section,
                "MaxSpeed",
                300f,
                "Maximum flight speed in metres per second. Caps the speed gained through "
                    + "Acceleration so the drone cannot tunnel through targets at extreme velocity."
            );
            MaxActiveDrones = cfg.Bind(
                Section,
                "MaxActiveDrones",
                1,
                "Maximum number of hunter drones a single player may have in flight at once. "
                    + "Set to 1 for the original one-at-a-time behaviour."
            );
        }
    }
}
