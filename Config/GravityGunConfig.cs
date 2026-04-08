using BepInEx.Configuration;
using UnityEngine.InputSystem;

namespace IssaPlugin
{
    public class GravityGunConfig
    {
        private const string Section = "GravityGun";

        public ConfigEntry<Key> GiveKey { get; private set; }
        public ConfigEntry<float> Uses { get; private set; }
        public ConfigEntry<float> SpawnWeight { get; private set; }
        public ConfigEntry<float> LockOnRange { get; private set; }
        public ConfigEntry<float> LockOnConeAngleDeg { get; private set; }
        public ConfigEntry<float> TetherRadius { get; private set; }
        public ConfigEntry<float> TetherDuration { get; private set; }
        public ConfigEntry<float> SpringForce { get; private set; }
        public ConfigEntry<float> MaxPullSpeed { get; private set; }
        public ConfigEntry<float> InputSendInterval { get; private set; }

        public GravityGunConfig(ConfigFile cfg, GlobalConfig global)
        {
            GiveKey = cfg.Bind(
                Section,
                "GiveKey",
                Key.NumpadDivide,
                "Hotkey to give yourself a Gravity Gun (debug/testing)."
            );
            Uses = cfg.Bind(Section, "Uses", 1f, "Number of uses per Gravity Gun pickup.");
            LockOnRange = cfg.Bind(
                Section,
                "LockOnRange",
                45f,
                "Maximum distance (units) at which the Gravity Gun can lock onto a target."
            );
            LockOnConeAngleDeg = cfg.Bind(
                Section,
                "LockOnConeAngleDeg",
                40f,
                "Half-angle (degrees) of the aim cone used to filter lock-on candidates."
            );
            TetherRadius = cfg.Bind(
                Section,
                "TetherRadius",
                20f,
                "Distance (units) at which the target orbits the wielder while tethered."
            );
            TetherDuration = cfg.Bind(
                Section,
                "TetherDuration",
                5f,
                "Maximum tether duration in seconds before automatic release."
            );
            SpringForce = cfg.Bind(
                Section,
                "SpringForce",
                12f,
                "Spring stiffness coefficient; higher values pull the target faster."
            );
            MaxPullSpeed = cfg.Bind(
                Section,
                "MaxPullSpeed",
                18f,
                "Maximum velocity change (m/s) applied to the target per physics tick."
            );
            InputSendInterval = cfg.Bind(
                Section,
                "InputSendInterval",
                0.05f,
                "Interval (seconds) between aim-tick messages sent to the server (~20 Hz)."
            );

            SpawnWeight = global.BindSpawnWeight(
                cfg,
                121,
                "GravityGunSpawnWeight",
                10f,
                "Override spawn weight for the Gravity Gun."
            );
        }
    }
}
