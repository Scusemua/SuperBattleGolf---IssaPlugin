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
        public static RocketTetherGrenadeConfig RocketTetherGrenade { get; private set; }
        public static WindStormConfig WindStorm { get; private set; }

        public static void Initialize(ConfigFile cfg)
        {
            // Global must come first — GlobalConfig.BindAllItemPoolWeights binds all per-pool weights centrally.
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
            RocketTetherGrenade = new RocketTetherGrenadeConfig(cfg, Global);
            WindStorm = new WindStormConfig(cfg, Global);
        }

        // ── Pass-through helper methods (from GlobalConfig) ───────────────────

        public static bool GetItemEnabled(ItemType itemType) => Global.GetItemEnabled(itemType);

        public static void SetItemEnabled(ItemType itemType, bool enabled) =>
            Global.SetItemEnabled(itemType, enabled);

        public static float GetItemPoolWeight(ItemType t, int pool) =>
            Global.GetItemPoolWeight(t, pool);

        public static void SetItemPoolWeight(ItemType t, int pool, float v) =>
            Global.SetItemPoolWeight(t, pool, v);

        public static bool GetItemWarningEnabled(ItemType itemType) =>
            Global.GetItemWarningEnabled(itemType);
    }
}
