using BepInEx.Configuration;
using UnityEngine.InputSystem;

namespace IssaPlugin
{
    public class DonutConfig
    {
        private const string Section = "Donut";

        public ConfigEntry<Key> GiveKey { get; private set; }
        public ConfigEntry<float> Uses { get; private set; }
        public ConfigEntry<float> SpawnWeight { get; private set; }
        public ConfigEntry<float> Speed { get; private set; }
        public ConfigEntry<float> Altitude { get; private set; }
        public ConfigEntry<float> TerrainFollowSpeed { get; private set; }
        public ConfigEntry<float> TurnSpeed { get; private set; }
        public ConfigEntry<float> CameraPitch { get; private set; }
        public ConfigEntry<float> CameraDistance { get; private set; }
        public ConfigEntry<float> MouseSensitivity { get; private set; }
        public ConfigEntry<float> Duration { get; private set; }
        public ConfigEntry<float> LaserUses { get; private set; }
        public ConfigEntry<float> LaserAnticipationDuration { get; private set; }
        public ConfigEntry<float> LaserCooldown { get; private set; }
        public ConfigEntry<float> HitsToDestroy { get; private set; }
        public ConfigEntry<float> CrashImpactForce { get; private set; }
        public ConfigEntry<float> CrashDownwardForce { get; private set; }
        public ConfigEntry<float> CrashTorque { get; private set; }
        public ConfigEntry<float> CrashExplosionScale { get; private set; }

        public DonutConfig(ConfigFile cfg, GlobalConfig global)
        {
            GiveKey = cfg.Bind(Section, "GiveKey", Key.Numpad2, "Debug key to add the Donut to your inventory.");
            Uses = cfg.Bind(Section, "Uses", 1f, "Number of Donut uses per pickup.");
            Speed = cfg.Bind(Section, "Speed", 30f, "Horizontal movement speed of the Donut in units per second.");
            Altitude = cfg.Bind(Section, "Altitude", 20f, "Height above terrain the Donut hovers at.");
            TerrainFollowSpeed = cfg.Bind(Section, "TerrainFollowSpeed", 5f, "How quickly the Donut adjusts its altitude to match terrain changes (lerp speed).");
            TurnSpeed = cfg.Bind(Section, "TurnSpeed", 8f, "How quickly the Donut rotates to face the movement direction (Slerp speed).");
            CameraPitch = cfg.Bind(Section, "CameraPitch", 60f, "Camera pitch angle in degrees during the Donut session (0 = horizontal, 90 = straight down).");
            CameraDistance = cfg.Bind(Section, "CameraDistance", 40f, "Camera distance addition from the Donut during the session.");
            MouseSensitivity = cfg.Bind(Section, "MouseSensitivity", 0.2f, "Mouse X sensitivity for rotating the Donut orbit camera.");
            Duration = cfg.Bind(Section, "Duration", 30f, "How many seconds the Donut session lasts before automatically ending.");
            LaserUses = cfg.Bind(Section, "LaserUses", 3f, "How many orbital laser strikes the player can fire during a Donut session.");
            LaserAnticipationDuration = cfg.Bind(Section, "LaserAnticipationDuration", 1.5f, "Seconds of anticipation before the orbital laser fires. The laser tracks the Donut during this window.");
            LaserCooldown = cfg.Bind(Section, "LaserCooldown", 3f, "Minimum seconds between orbital laser fires.");
            HitsToDestroy = cfg.Bind(Section, "HitsToDestroy", 1f, "Rocket hits required to shoot down the Donut. Set to 0 to make it invincible.");
            CrashImpactForce = cfg.Bind(Section, "CrashImpactForce", 500f, "Impulse force applied to the Donut in the direction of the rocket hit when shot down.");
            CrashDownwardForce = cfg.Bind(Section, "CrashDownwardForce", 15f, "Impulse force applied to the Donut in the downward direction.");
            CrashTorque = cfg.Bind(Section, "CrashTorque", 1.2f, "Magnitude of the random tumble torque applied to the Donut when shot down.");
            CrashExplosionScale = cfg.Bind(Section, "CrashExplosionScale", 4.0f, "Explosion scale multiplier for when a Donut crashes. Affects blast radius, knockback, and VFX size.");

            SpawnWeight = global.BindSpawnWeight(cfg, 107, "DonutWeight", 5f, "Override spawn weight for the Donut.");
        }
    }
}
