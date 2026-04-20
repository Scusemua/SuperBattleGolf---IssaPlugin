using BepInEx.Configuration;
using UnityEngine.InputSystem;

namespace IssaPlugin
{
    public class BoobyTrapConfig
    {
        private const string Section = "BoobyTrap";

        public ConfigEntry<Key> GiveKey { get; private set; }
        public ConfigEntry<float> Uses { get; private set; }
        public ConfigEntry<float> SpawnWeight { get; private set; }
        public ConfigEntry<float> Range { get; private set; }
        public ConfigEntry<float> AimConeAngle { get; private set; }
        public ConfigEntry<float> ExplosionScale { get; private set; }
        public ConfigEntry<bool> SelfDamage { get; private set; }

        public BoobyTrapConfig(ConfigFile cfg, GlobalConfig global)
        {
            GiveKey = cfg.Bind(
                Section,
                "GiveKey",
                Key.None,
                "Debug key to add the Booby Trap item to your inventory."
            );
            Uses = cfg.Bind(Section, "Uses", 3f, "Number of uses per Booby Trap pickup.");
            SpawnWeight = cfg.Bind(
                Section,
                "SpawnWeight",
                8f,
                "Default spawn-pool weight for the Booby Trap item."
            );
            Range = cfg.Bind(
                Section,
                "Range",
                8f,
                "Maximum distance in metres to trap a world object (item box or golf cart)."
            );
            AimConeAngle = cfg.Bind(
                Section,
                "AimConeAngle",
                60f,
                "Half-angle (degrees) of the aim cone used to select the best world target. "
                    + "Only objects within this cone around the camera forward are considered."
            );
            ExplosionScale = cfg.Bind(
                Section,
                "ExplosionScale",
                0.8f,
                "Scale multiplier applied to the booby-trap explosion radius and force."
            );
            SelfDamage = cfg.Bind(
                Section,
                "SelfDamage",
                false,
                "If true, the player who placed the trap can also trigger it."
            );
        }
    }
}
