using BepInEx.Configuration;
using IssaPlugin.Items;

namespace IssaPlugin
{
    /// <summary>
    /// Static root container for all plugin configuration. Call <see cref="Initialize"/> once
    /// from Plugin.cs, then access config via ModConfig.SectionName.Property.
    /// </summary>
    public static class ModConfig
    {
        public const int MaxTiers = 5;

        public static GlobalConfig Global { get; private set; }
        public static BaseballBatConfig BaseballBat { get; private set; }
        public static StealthBomberConfig StealthBomber { get; private set; }
        public static PredatorMissileConfig PredatorMissile { get; private set; }
        public static AC130Config AC130 { get; private set; }
        public static FreezeConfig Freeze { get; private set; }
        public static LowGravityConfig LowGravity { get; private set; }
        public static SniperRifleConfig SniperRifle { get; private set; }
        public static DonutConfig Donut { get; private set; }
        public static JavelinConfig Javelin { get; private set; }
        public static StickyGrenadeConfig StickyGrenade { get; private set; }
        public static BearConfig Bear { get; private set; }
        public static NukeConfig Nuke { get; private set; }
        public static BlackHoleGrenadeConfig BlackHoleGrenade { get; private set; }
        public static PlaceableWallConfig PlaceableWall { get; private set; }
        public static AK47Config AK47 { get; private set; }
        public static HarrierConfig Harrier { get; private set; }
        public static PositionSwapConfig PositionSwap { get; private set; }
        public static PoisonJarConfig PoisonJar { get; private set; }
        public static DroneSwarmConfig DroneSwarm { get; private set; }
        public static RedBullConfig RedBull { get; private set; }
        public static SuperDonutConfig SuperDonut { get; private set; }
        public static GravityGunConfig GravityGun { get; private set; }
        public static RocketTetherConfig RocketTether { get; private set; }
        public static JetpackConfig Jetpack { get; private set; }
        public static TeleporterConfig Teleporter { get; private set; }
        public static SpinachConfig Spinach { get; private set; }
        public static FlamethrowerConfig Flamethrower { get; private set; }

        public static void Initialize(ConfigFile cfg)
        {
            // Global must come first — item configs call global.BindSpawnWeight
            Global = new GlobalConfig(cfg);

            BaseballBat = new BaseballBatConfig(cfg, Global);
            StealthBomber = new StealthBomberConfig(cfg, Global);
            PredatorMissile = new PredatorMissileConfig(cfg, Global);
            AC130 = new AC130Config(cfg, Global);
            Freeze = new FreezeConfig(cfg, Global);
            LowGravity = new LowGravityConfig(cfg, Global);
            SniperRifle = new SniperRifleConfig(cfg, Global);
            Donut = new DonutConfig(cfg, Global);
            Javelin = new JavelinConfig(cfg, Global);
            StickyGrenade = new StickyGrenadeConfig(cfg, Global);
            Bear = new BearConfig(cfg, Global);
            Nuke = new NukeConfig(cfg, Global);
            BlackHoleGrenade = new BlackHoleGrenadeConfig(cfg, Global);
            PlaceableWall = new PlaceableWallConfig(cfg, Global);
            AK47 = new AK47Config(cfg, Global);
            Harrier = new HarrierConfig(cfg, Global);
            PositionSwap = new PositionSwapConfig(cfg, Global);
            PoisonJar = new PoisonJarConfig(cfg, Global);
            DroneSwarm = new DroneSwarmConfig(cfg, Global);
            RedBull = new RedBullConfig(cfg, Global);
            SuperDonut = new SuperDonutConfig(cfg, Global);
            GravityGun = new GravityGunConfig(cfg, Global);
            RocketTether = new RocketTetherConfig(cfg, Global);
            Jetpack = new JetpackConfig(cfg, Global);
            Teleporter = new TeleporterConfig(cfg, Global);
            Spinach = new SpinachConfig(cfg, Global);
            Flamethrower = new FlamethrowerConfig(cfg, Global);
        }

        // ── Pass-through helper methods (from GlobalConfig) ───────────────────

        public static bool GetItemEnabled(ItemType itemType) => Global.GetItemEnabled(itemType);
        public static void SetItemEnabled(ItemType itemType, bool enabled) => Global.SetItemEnabled(itemType, enabled);

        public static int GetItemTier(ItemType itemType) => Global.GetItemTier(itemType);
        public static void SetItemTier(ItemType itemType, int tier) => Global.SetItemTier(itemType, tier);

        public static bool GetItemSpawnWeightOverrideEnabled(ItemType t) => Global.GetItemSpawnWeightOverrideEnabled(t);
        public static float GetItemSpawnWeightOverrideValue(ItemType t) => Global.GetItemSpawnWeightOverrideValue(t);
        public static void SetItemSpawnWeightOverride(ItemType itemType, bool enabled, float value) => Global.SetItemSpawnWeightOverride(itemType, enabled, value);

        public static bool GetTierEnabled(int tier) => Global.GetTierEnabled(tier);
        public static void SetTierEnabled(int tier, bool value) => Global.SetTierEnabled(tier, value);

        public static float GetTierSpawnWeight(int tier) => Global.GetTierSpawnWeight(tier);
        public static void SetTierSpawnWeight(int tier, float value) => Global.SetTierSpawnWeight(tier, value);

        public static float GetTierMinDistance(int tier) => Global.GetTierMinDistance(tier);
        public static void SetTierMinDistance(int tier, float value) => Global.SetTierMinDistance(tier, value);

        public static int GetTierMinPlace(int tier) => Global.GetTierMinPlace(tier);
        public static void SetTierMinPlace(int tier, int value) => Global.SetTierMinPlace(tier, value);

        public static bool GetItemWarningEnabled(ItemType itemType) => Global.GetItemWarningEnabled(itemType);
    }
}
