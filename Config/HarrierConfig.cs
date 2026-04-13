using BepInEx.Configuration;
using UnityEngine.InputSystem;

namespace IssaPlugin
{
    public class HarrierConfig
    {
        private const string Section = "HarrierJet";

        public ConfigEntry<Key> GiveKey { get; private set; }
        public ConfigEntry<float> Uses { get; private set; }
        public ConfigEntry<float> Altitude { get; private set; }
        public ConfigEntry<float> Duration { get; private set; }
        public ConfigEntry<float> FireInterval { get; private set; }
        public ConfigEntry<float> ApproachDistance { get; private set; }
        public ConfigEntry<float> ApproachSpeed { get; private set; }
        public ConfigEntry<float> DriftSpeed { get; private set; }
        public ConfigEntry<float> HoverRadius { get; private set; }
        public ConfigEntry<float> ExplosionScale { get; private set; }
        public ConfigEntry<float> RocketJitter { get; private set; }
        public ConfigEntry<bool> FriendlyFire { get; private set; }
        public ConfigEntry<float> HitsToDestroy { get; private set; }
        public ConfigEntry<bool> FirearmsCanHit { get; private set; }
        public ConfigEntry<float> CrashExplosionScale { get; private set; }

        public HarrierConfig(ConfigFile cfg, GlobalConfig global)
        {
            GiveKey = cfg.Bind(
                Section,
                "GiveKey",
                Key.Numpad9,
                "Debug key to add the Harrier Jet to your inventory."
            );
            Uses = cfg.Bind(Section, "Uses", 1f, "Number of Harrier Jet uses per pickup.");
            Altitude = cfg.Bind(
                Section,
                "Altitude",
                175f,
                "Height above the map centre the Harrier hovers at while attacking."
            );
            Duration = cfg.Bind(
                Section,
                "Duration",
                30f,
                "How many seconds the Harrier remains on-station before flying out."
            );
            FireInterval = cfg.Bind(
                Section,
                "FireInterval",
                2.5f,
                "Seconds between each autonomous rocket fired by the Harrier."
            );
            ApproachDistance = cfg.Bind(
                Section,
                "ApproachDistance",
                600f,
                "How far away the Harrier spawns before flying in to its hover point."
            );
            ApproachSpeed = cfg.Bind(
                Section,
                "ApproachSpeed",
                100f,
                "Speed in units per second during fly-in and fly-out."
            );
            DriftSpeed = cfg.Bind(
                Section,
                "DriftSpeed",
                8f,
                "Units per second the Harrier drifts toward the player concentration centroid."
            );
            HoverRadius = cfg.Bind(
                Section,
                "HoverRadius",
                30f,
                "Maximum distance the Harrier can drift from its initial hover centre."
            );
            ExplosionScale = cfg.Bind(
                Section,
                "ExplosionScale",
                1.5f,
                "Scale multiplier applied to each rocket explosion from the Harrier."
            );
            RocketJitter = cfg.Bind(
                Section,
                "RocketJitter",
                4f,
                "Angular spread in degrees applied to each rocket. Higher = less accurate."
            );
            FriendlyFire = cfg.Bind(
                Section,
                "FriendlyFire",
                false,
                "If false, the Harrier will not target the player who summoned it."
            );
            HitsToDestroy = cfg.Bind(
                Section,
                "HitsToDestroy",
                3f,
                "Number of direct hits required to shoot down the Harrier. Set to 0 to make it invincible."
            );
            FirearmsCanHit = cfg.Bind(
                Section,
                "FirearmsCanHit",
                true,
                "If true, firearms (sniper rifle, elephant gun, dueling pistol, AK-47) can contribute hits toward shooting down the Harrier."
            );
            CrashExplosionScale = cfg.Bind(
                Section,
                "CrashExplosionScale",
                3f,
                "Scale multiplier for the explosion spawned when the Harrier crashes into the ground."
            );
        }
    }
}
