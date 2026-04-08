using BepInEx.Configuration;
using UnityEngine.InputSystem;

namespace IssaPlugin
{
    public class NukeConfig
    {
        private const string Section = "Nuke";

        public ConfigEntry<Key> GiveKey { get; private set; }
        public ConfigEntry<float> Uses { get; private set; }
        public ConfigEntry<float> SpawnWeight { get; private set; }
        public ConfigEntry<float> DropHeight { get; private set; }
        public ConfigEntry<float> DropSpeed { get; private set; }
        public ConfigEntry<float> ExplosionScale { get; private set; }
        public ConfigEntry<float> SkyBlastForce { get; private set; }
        public ConfigEntry<float> SkyBlastRadius { get; private set; }
        public ConfigEntry<float> SkyBlastVerticalBias { get; private set; }
        public ConfigEntry<float> ExplosionVfxDuration { get; private set; }
        public ConfigEntry<float> ExplosionVfxScale { get; private set; }
        public ConfigEntry<bool> ExcludeThrower { get; private set; }

        public NukeConfig(ConfigFile cfg, GlobalConfig global)
        {
            GiveKey = cfg.Bind(
                Section,
                "GiveKey",
                Key.Numpad6,
                "Debug key to add the Nuke to your inventory."
            );
            Uses = cfg.Bind(Section, "Uses", 1f, "Number of uses per Nuke pickup.");
            DropHeight = cfg.Bind(
                Section,
                "DropHeight",
                300f,
                "Units above the map centre at which the nuke bomb spawns."
            );
            DropSpeed = cfg.Bind(
                Section,
                "DropSpeed",
                80f,
                "Downward speed of the falling nuke bomb in units per second."
            );
            SkyBlastForce = cfg.Bind(
                Section,
                "SkyBlastForce",
                350f,
                "Extra upward impulse force applied to all rigidbodies within SkyBlastRadius after detonation. Stacks on top of the standard explosion knockback."
            );
            SkyBlastRadius = cfg.Bind(
                Section,
                "SkyBlastRadius",
                350f,
                "Radius (units) of the secondary sky blast. Should be large enough to cover the whole map so no one escapes."
            );
            SkyBlastVerticalBias = cfg.Bind(
                Section,
                "SkyBlastVerticalBias",
                0.7f,
                "Controls how much of the sky blast force goes upward versus outward (0 = all outward, 1 = all straight up). The horizontal direction is still relative to the explosion point, so players are pushed away from the blast site."
            );
            ExplosionVfxDuration = cfg.Bind(
                Section,
                "ExplosionVfxDuration",
                8f,
                "Seconds before the nuke explosion VFX prefab is destroyed on each client."
            );
            ExplosionVfxScale = cfg.Bind(
                Section,
                "ExplosionVfxScale",
                5.0f,
                "Uniform scale applied to the nuke explosion VFX transform on each client. Increase this to make the particle systems appear larger."
            );
            ExcludeThrower = cfg.Bind(
                Section,
                "ExcludeThrower",
                true,
                "If true, the sky blast does not apply force to the player who activated the nuke."
            );

            SpawnWeight = global.BindSpawnWeight(
                cfg,
                111,
                "NukeWeight",
                5f,
                "Override spawn weight for the Nuke."
            );
            ExplosionScale = cfg.Bind(
                "Explosions",
                "NukeScale",
                6.0f,
                "Explosion scale multiplier for the Nuke's detonation rocket. Affects blast radius, knockback force, and VFX size."
            );
        }
    }
}
