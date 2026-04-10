using BepInEx.Configuration;
using UnityEngine.InputSystem;

namespace IssaPlugin
{
    public class RocketTetherGrenadeConfig
    {
        private const string Section = "RocketTetherGrenade";

        public ConfigEntry<Key> GiveKey { get; private set; }
        public ConfigEntry<float> Uses { get; private set; }
        public ConfigEntry<float> SpawnWeight { get; private set; }
        public ConfigEntry<bool> ExcludeThrower { get; private set; }

        // ── Throw arc ─────────────────────────────────────────────────────────
        public ConfigEntry<float> ThrowSpeed { get; private set; }
        public ConfigEntry<float> LobAngle { get; private set; }

        // ── Area-of-effect on landing ─────────────────────────────────────────
        public ConfigEntry<float> BlastRadius { get; private set; }
        public ConfigEntry<int> MaxVictims { get; private set; }

        // ── Per-victim tether (independently tunable from the single-target item) ──
        public ConfigEntry<float> TetherDuration { get; private set; }
        public ConfigEntry<float> RocketSpeed { get; private set; }
        public ConfigEntry<float> SpringForce { get; private set; }
        public ConfigEntry<float> MaxPullSpeed { get; private set; }
        public ConfigEntry<float> NaturalLength { get; private set; }

        // ── Per-victim explosion ──────────────────────────────────────────────
        public ConfigEntry<float> ExplosionForce { get; private set; }
        public ConfigEntry<float> ExplosionRadius { get; private set; }

        public RocketTetherGrenadeConfig(ConfigFile cfg, GlobalConfig global)
        {
            GiveKey = cfg.Bind(
                Section,
                "GiveKey",
                Key.None,
                "Hotkey to give yourself a Rocket Tether Grenade (debug/testing)."
            );
            Uses = cfg.Bind(
                Section,
                "Uses",
                1f,
                "Number of uses per Rocket Tether Grenade pickup."
            );
            ThrowSpeed = cfg.Bind(
                Section,
                "ThrowSpeed",
                18f,
                "Initial speed (m/s) of the thrown grenade."
            );
            LobAngle = cfg.Bind(
                Section,
                "LobAngle",
                0.4f,
                "Upward angle added to the camera forward direction when throwing (0 = flat, 1 = straight up)."
            );
            BlastRadius = cfg.Bind(
                Section,
                "BlastRadius",
                12f,
                "Radius (units) within which players are tethered on landing."
            );
            MaxVictims = cfg.Bind(
                Section,
                "MaxVictims",
                4,
                "Maximum number of players that can be simultaneously tethered by one grenade."
            );
            ExcludeThrower = cfg.Bind(
                Section,
                "ExcludeThrower",
                true,
                "If true, the thrower cannot tether themselves with a grenade."
            );
            TetherDuration = cfg.Bind(
                Section,
                "TetherDuration",
                6f,
                "Duration (seconds) before each victim's rocket explodes."
            );
            RocketSpeed = cfg.Bind(
                Section,
                "RocketSpeed",
                35f,
                "Upward speed (m/s) of each victim's rocket during its flight."
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
                "Maximum velocity change (m/s) applied to each victim per physics tick."
            );
            NaturalLength = cfg.Bind(
                Section,
                "NaturalLength",
                5f,
                "Distance (units) below which no pull force is applied (rope slack)."
            );
            ExplosionForce = cfg.Bind(
                Section,
                "ExplosionForce",
                80f,
                "Peak velocity change (m/s, VelocityChange) at each victim's explosion centre."
            );
            ExplosionRadius = cfg.Bind(
                Section,
                "ExplosionRadius",
                50f,
                "Radius (units) within which each victim's explosion affects nearby players."
            );

            SpawnWeight = global.BindSpawnWeight(
                cfg,
                127,
                "RocketTetherGrenadeSpawnWeight",
                8f,
                "Override spawn weight for the Rocket Tether Grenade."
            );
        }
    }
}
