using BepInEx.Configuration;
using UnityEngine.InputSystem;

namespace IssaPlugin
{
    public class ExplosiveGolfBallsConfig
    {
        private const string Section = "ExplosiveGolfBalls";

        public ConfigEntry<float> Uses { get; private set; }
        public ConfigEntry<bool> LockOnOnly { get; private set; }
        public ConfigEntry<float> ExplosionScale { get; private set; }
        public ConfigEntry<Key> GiveKey { get; private set; }

        public ExplosiveGolfBallsConfig(ConfigFile cfg, GlobalConfig global)
        {
            Uses = cfg.Bind(
                Section,
                "Uses",
                3f,
                "Number of swings that will produce an exploding ball."
            );
            LockOnOnly = cfg.Bind(
                Section,
                "LockOnOnly",
                true,
                "When true, the explosion only triggers on lock-on (overcharge) shots."
            );
            ExplosionScale = cfg.Bind(
                Section,
                "ExplosionScale",
                1f,
                "Scale multiplier applied to the explosion radius and force."
            );
            GiveKey = cfg.Bind(
                Section,
                "GiveKey",
                Key.None,
                "Debug key to add Explosive Golf Balls to your inventory."
            );
        }
    }
}
