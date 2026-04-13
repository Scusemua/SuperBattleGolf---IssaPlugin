using BepInEx.Configuration;
using UnityEngine.InputSystem;

namespace IssaPlugin
{
    public class StealthBomberConfig
    {
        private const string Section = "StealthBomber";

        public ConfigEntry<float> Altitude { get; private set; }
        public ConfigEntry<float> Speed { get; private set; }
        public ConfigEntry<float> RocketInterval { get; private set; }
        public ConfigEntry<float> Spread { get; private set; }
        public ConfigEntry<float> Uses { get; private set; }
        public ConfigEntry<Key> GiveKey { get; private set; }
        public ConfigEntry<float> WaitTime { get; private set; }
        public ConfigEntry<float> StripLength { get; private set; }
        public ConfigEntry<float> RocketAngularJitter { get; private set; }
        public ConfigEntry<float> RocketSpawnDepth { get; private set; }
        public ConfigEntry<float> TargetingZoomSpeed { get; private set; }
        public ConfigEntry<float> TargetMoveSpeed { get; private set; }
        public ConfigEntry<float> TargetRotateSpeed { get; private set; }
        public ConfigEntry<float> ApproachDistance { get; private set; }
        public ConfigEntry<float> HitsToDestroy { get; private set; }
        public ConfigEntry<bool> FirearmsCanHit { get; private set; }
        public ConfigEntry<float> CrashImpactForce { get; private set; }
        public ConfigEntry<float> CrashDownwardForce { get; private set; }
        public ConfigEntry<float> CrashTorque { get; private set; }
        public ConfigEntry<float> ExplosionScale { get; private set; }

        public StealthBomberConfig(ConfigFile cfg, GlobalConfig global)
        {
            Altitude = cfg.Bind(
                Section,
                "Altitude",
                60f,
                "Height above the map the bombing run flies at."
            );
            Speed = cfg.Bind(
                Section,
                "Speed",
                35f,
                "Speed of the bombing run in units per second."
            );
            ApproachDistance = cfg.Bind(
                Section,
                "ApproachDistance",
                300f,
                "How far away the bomber visual spawns from the targeting strip in units."
            );
            RocketInterval = cfg.Bind(
                Section,
                "RocketInterval",
                0.75f,
                "Seconds between each rocket drop during a bombing run."
            );
            Spread = cfg.Bind(
                Section,
                "Spread",
                25f,
                new ConfigDescription(
                    "Random lateral spread in units for each rocket's drop position.",
                    new AcceptableValueRange<float>(25f, 100f)
                )
            );
            Uses = cfg.Bind(Section, "Uses", 1f, "Number of bombing runs per pickup.");
            GiveKey = cfg.Bind(
                Section,
                "GiveKey",
                Key.F8,
                "Key to press to add the stealth bomber to your inventory."
            );
            WaitTime = cfg.Bind(
                Section,
                "WaitTime",
                1.5f,
                "Seconds to wait before starting the bombing run."
            );
            StripLength = cfg.Bind(
                Section,
                "StripLength",
                300f,
                "Length of the targeting strip in units."
            );
            TargetingZoomSpeed = cfg.Bind(
                Section,
                "TargetingZoomSpeed",
                5f,
                "Speed at which the camera zooms in/out during bomber targeting."
            );
            RocketAngularJitter = cfg.Bind(
                Section,
                "RocketAngularJitter",
                0.8f,
                "Random angular jitter in degrees for each rocket's rotation."
            );
            RocketSpawnDepth = cfg.Bind(
                Section,
                "RocketSpawnDepth",
                5f,
                "How far below the bomber (in units) rockets spawn, to avoid colliding with the bomber on creation."
            );
            TargetMoveSpeed = cfg.Bind(
                Section,
                "TargetMoveSpeed",
                50f,
                "How fast the targeting strip moves with WASD."
            );
            TargetRotateSpeed = cfg.Bind(
                Section,
                "TargetRotateSpeed",
                90f,
                "Rotation speed of the targeting strip in degrees per second (Q/E)."
            );
            HitsToDestroy = cfg.Bind(
                Section,
                "HitsToDestroy",
                1f,
                "Hits required to shoot down the bomber and cancel its run. Set to 0 to make it invincible."
            );
            FirearmsCanHit = cfg.Bind(
                Section,
                "FirearmsCanHit",
                true,
                "If true, firearms (sniper rifle, elephant gun, dueling pistol, AK-47) can contribute hits toward shooting down the stealth bomber."
            );
            CrashImpactForce = cfg.Bind(
                Section,
                "CrashImpactForce",
                500f,
                "Impulse force applied to the stealth bomber in the direction of the rocket hit when shot down."
            );
            CrashDownwardForce = cfg.Bind(
                Section,
                "CrashDownwardForce",
                15f,
                "Impulse force applied to the stealth bomber in the downward direction."
            );
            CrashTorque = cfg.Bind(
                Section,
                "CrashTorque",
                1.2f,
                "Magnitude of the random tumble torque applied to the stealth bomber when shot down."
            );
            ExplosionScale = cfg.Bind(
                "Explosions",
                "StealthBomberScale",
                1.5f,
                "Multiplier for Stealth Bomber rocket explosions. Affects blast radius, knockback, and VFX size."
            );
        }
    }
}
