using BepInEx.Configuration;
using UnityEngine.InputSystem;

namespace IssaPlugin
{
    public class RocketTetherConfig
    {
        private const string Section = "RocketTether";

        public ConfigEntry<Key> GiveKey { get; private set; }
        public ConfigEntry<float> Uses { get; private set; }
        public ConfigEntry<float> SpawnWeight { get; private set; }
        public ConfigEntry<float> LockOnRange { get; private set; }
        public ConfigEntry<float> LockOnConeAngleDeg { get; private set; }
        public ConfigEntry<float> TetherDuration { get; private set; }
        public ConfigEntry<float> SpringForce { get; private set; }
        public ConfigEntry<float> MaxPullSpeed { get; private set; }
        public ConfigEntry<float> NaturalLength { get; private set; }
        public ConfigEntry<float> RocketSpeed { get; private set; }
        public ConfigEntry<float> ExplosionForce { get; private set; }
        public ConfigEntry<float> ExplosionRadius { get; private set; }

        public RocketTetherConfig(ConfigFile cfg, GlobalConfig global)
        {
            GiveKey = cfg.Bind(
                Section,
                "GiveKey",
                Key.NumpadMultiply,
                "Hotkey to give yourself a Rocket Tether (debug/testing)."
            );
            Uses = cfg.Bind(Section, "Uses", 1f, "Number of uses per Rocket Tether pickup.");
            LockOnRange = cfg.Bind(
                Section,
                "LockOnRange",
                40f,
                "Maximum distance (units) at which the Rocket Tether can lock onto a target."
            );
            LockOnConeAngleDeg = cfg.Bind(
                Section,
                "LockOnConeAngleDeg",
                60f,
                "Full angle (degrees) of the aim cone used to filter lock-on candidates."
            );
            TetherDuration = cfg.Bind(
                Section,
                "TetherDuration",
                8f,
                "Duration in seconds before the tether automatically disconnects."
            );
            SpringForce = cfg.Bind(
                Section,
                "SpringForce",
                5f,
                "Spring stiffness coefficient; multiplied by stretch distance to compute pull speed."
            );
            MaxPullSpeed = cfg.Bind(
                Section,
                "MaxPullSpeed",
                20f,
                "Maximum velocity change (m/s) applied to each player per physics tick."
            );
            NaturalLength = cfg.Bind(
                Section,
                "NaturalLength",
                5f,
                "Distance (units) below which no force is applied. Set >0 for a rope with slack."
            );
            RocketSpeed = cfg.Bind(
                Section,
                "RocketSpeed",
                35f,
                "Upward speed (m/s) of the rocket during its flight."
            );
            ExplosionForce = cfg.Bind(
                Section,
                "ExplosionForce",
                100f,
                "Peak velocity change (m/s, VelocityChange) applied at the explosion centre."
            );
            ExplosionRadius = cfg.Bind(
                Section,
                "ExplosionRadius",
                50f,
                "Radius (units) within which the explosion affects nearby players."
            );

            SpawnWeight = global.BindSpawnWeight(
                cfg,
                122,
                "RocketTetherSpawnWeight",
                10f,
                "Override spawn weight for the Rocket Tether."
            );
        }
    }
}
