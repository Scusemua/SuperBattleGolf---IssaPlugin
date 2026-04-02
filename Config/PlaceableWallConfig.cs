using BepInEx.Configuration;
using UnityEngine.InputSystem;

namespace IssaPlugin
{
    public class PlaceableWallConfig
    {
        private const string Section = "PlaceableWall";

        public ConfigEntry<Key> GiveKey { get; private set; }
        public ConfigEntry<float> Uses { get; private set; }
        public ConfigEntry<float> SpawnWeight { get; private set; }
        public ConfigEntry<float> MaxPlacementDistance { get; private set; }
        public ConfigEntry<float> MinHoleDistance { get; private set; }
        public ConfigEntry<float> HealthPoints { get; private set; }
        public ConfigEntry<float> VelocityImpactFactor { get; private set; }
        public ConfigEntry<float> TorsionMultiplier { get; private set; }
        public ConfigEntry<float> RotationStep { get; private set; }
        public ConfigEntry<float> DebrisLifetime { get; private set; }
        public ConfigEntry<float> RocketExplosionForce { get; private set; }
        public ConfigEntry<float> DamageGolfClub { get; private set; }
        public ConfigEntry<float> DamageBaseballBat { get; private set; }

        public PlaceableWallConfig(ConfigFile cfg, GlobalConfig global)
        {
            GiveKey = cfg.Bind(Section, "GiveKey", Key.Numpad8, "Debug key to add the Placeable Wall to your inventory.");
            Uses = cfg.Bind(Section, "Uses", 1f, "Number of wall placements per pickup.");
            MaxPlacementDistance = cfg.Bind(Section, "MaxPlacementDistance", 20f, "Maximum distance (units) from the player's camera at which a wall can be placed.");
            MinHoleDistance = cfg.Bind(Section, "MinHoleDistance", 3f, "Minimum XZ distance (units) from the hole centre that a wall may be placed. Prevents blocking the hole entirely. Set to 0 to disable the check.");
            HealthPoints = cfg.Bind(Section, "HealthPoints", 3f, "Number of golf club or baseball bat hits required to destroy the wall. Rocket hits always destroy in one shot.");
            VelocityImpactFactor = cfg.Bind(Section, "VelocityImpactFactor", 2.5f, "Degree to which the velocity of an object colliding with the placeable wall will damage it. Default: 2.5.");
            TorsionMultiplier = cfg.Bind(Section, "TorsionMultiplier", 0.002f, "Degree to which torque (spin) damages the bricks of the placeable wall. Default: 0.002.");
            RotationStep = cfg.Bind(Section, "RotationStep", 45f, "Degrees the wall rotates per scroll-wheel tick during the placement preview. Scroll up rotates clockwise, scroll down rotates counter-clockwise.");
            DebrisLifetime = cfg.Bind(Section, "DebrisLifetime", 30f, "Seconds before detached wall debris (bricks/pillars) are automatically destroyed.");
            RocketExplosionForce = cfg.Bind(Section, "RocketExplosionForce", 600f, "Impulse force (per brick) applied by a rocket explosion to detached wall debris.");
            DamageGolfClub = cfg.Bind(Section, "DamageGolfClub", 1f, "Damage dealt to a wall chunk per golf club swing.");
            DamageBaseballBat = cfg.Bind(Section, "DamageBaseballBat", 2f, "Damage dealt to a wall chunk per baseball bat swing.");

            SpawnWeight = global.BindSpawnWeight(cfg, 113, "PlaceableWallWeight", 15f, "Override spawn weight for the Placeable Wall.");
        }
    }
}
