using System.Collections.Generic;
using BepInEx.Configuration;
using IssaPlugin.Items;
using UnityEngine.InputSystem;

namespace IssaPlugin
{
    public class GlobalConfig
    {
        // ── IssaPlugin section ────────────────────────────────────────────────
        public ConfigEntry<bool> CustomItemSpawnsEnabled { get; private set; }
        public ConfigEntry<float> CustomItemSpawnRate { get; private set; }
        public ConfigEntry<bool> AllowHotkeyItemGiving { get; private set; }
        public ConfigEntry<bool> BloodEffectsEnabled { get; private set; }
        public ConfigEntry<float> NumInventorySlots { get; private set; }

        // ── First Place Star ──────────────────────────────────────────────────
        public ConfigEntry<bool> FirstPlaceStarEnabled { get; private set; }
        public ConfigEntry<float> FirstPlaceStarHeight { get; private set; }

        // ── Diagnostics section ───────────────────────────────────────────────
        public ConfigEntry<bool> NetworkDiagnosticsEnabled { get; private set; }
        public ConfigEntry<float> NetworkDiagnosticsInterval { get; private set; }
        public ConfigEntry<int> NetworkDiagnosticsTopMessages { get; private set; }
        public ConfigEntry<bool> BomberOverlayEnabled { get; private set; }
        public ConfigEntry<bool> PlayerBoxOverlayEnabled { get; private set; }
        public ConfigEntry<bool> CustomVfxEnabled { get; private set; }
        public ConfigEntry<bool> PerfDiagnosticsEnabled { get; private set; }
        public ConfigEntry<bool> ModCpuProfilingEnabled { get; private set; }
        public ConfigEntry<float> PerfDiagnosticsInterval { get; private set; }
        public ConfigEntry<int> PerfDiagnosticsTopObjects { get; private set; }

        // ── UI section ────────────────────────────────────────────────────────
        public ConfigEntry<Key> SpawnConfigUIKey { get; private set; }

        // ── Warnings section ──────────────────────────────────────────────────
        public ConfigEntry<bool> WarningsEnabled { get; private set; }
        public ConfigEntry<float> WarningDuration { get; private set; }
        public ConfigEntry<bool> WarningShowForSelf { get; private set; }
        public ConfigEntry<bool> WarningSymbolEnabled { get; private set; }
        public ConfigEntry<float> WarningPrefabScale { get; private set; }
        public ConfigEntry<float> WarningSymbolAlpha { get; private set; }
        public ConfigEntry<float> WarningPiPScale { get; private set; }

        // ── Per-item enabled flags ────────────────────────────────────────────
        private readonly Dictionary<int, ConfigEntry<bool>> _itemEnabledEntries = new();

        public bool GetItemEnabled(ItemType itemType) =>
            _itemEnabledEntries.TryGetValue((int)itemType, out var e) ? e.Value : true;

        public void SetItemEnabled(ItemType itemType, bool enabled)
        {
            if (_itemEnabledEntries.TryGetValue((int)itemType, out var e))
                e.Value = enabled;
        }

        // ── Per-item warning flags ────────────────────────────────────────────
        private readonly Dictionary<int, ConfigEntry<bool>> _itemWarningEnabledEntries = new();

        public bool GetItemWarningEnabled(ItemType itemType) =>
            WarningsEnabled.Value
            && (_itemWarningEnabledEntries.TryGetValue((int)itemType, out var e) ? e.Value : false);

        // ── Pool index constants — match the base game's fixed pool indices exactly ──
        public const int PoolAhead = 0;
        public const int PoolLead = 1;
        public const int PoolBehind50 = 2;
        public const int PoolBehind125 = 3;
        public const int PoolBehind200 = 4;
        public const int PoolMobility = 5;

        private static readonly (int pool, string suffix)[] PoolSuffixes =
        {
            (PoolLead, "Lead"),
            (PoolBehind50, "Behind50"),
            (PoolBehind125, "Behind125"),
            (PoolBehind200, "Behind200"),
            (PoolAhead, "Ahead"),
            (PoolMobility, "Mobility"),
        };

        private readonly Dictionary<
            (int itemType, int pool),
            ConfigEntry<float>
        > _poolWeightEntries = new();

        public float GetItemPoolWeight(ItemType itemType, int poolIndex) =>
            _poolWeightEntries.TryGetValue(((int)itemType, poolIndex), out var e) ? e.Value : 0f;

        public void SetItemPoolWeight(ItemType itemType, int poolIndex, float value)
        {
            if (_poolWeightEntries.TryGetValue(((int)itemType, poolIndex), out var e))
                e.Value = value;
        }

        public GlobalConfig(ConfigFile cfg)
        {
            // ── IssaPlugin ────────────────────────────────────────────────────
            CustomItemSpawnsEnabled = cfg.Bind(
                "IssaPlugin",
                "Enabled",
                true,
                "Master kill-switch for allowing custom items to be spawned without having to set all spawn weights to 0."
            );

            CustomItemSpawnRate = cfg.Bind(
                "IssaPlugin",
                "CustomItemSpawnRate",
                0.5f,
                "Global multiplier applied to every custom item's spawn weight. 1.0 = default, 0.5 = half as frequent, 0.0 = never spawn."
            );

            AllowHotkeyItemGiving = cfg.Bind(
                "IssaPlugin",
                "AllowHotkeyItemGiving",
                false,
                "Whether non-host clients can use item hotkeys to give themselves items. The host is always exempt."
            );

            BloodEffectsEnabled = cfg.Bind(
                "IssaPlugin",
                "BloodEffectsEnabled",
                true,
                "Whether blood splatter effects are shown when players are hit by guns. Each client sets this independently."
            );

            NumInventorySlots = cfg.Bind(
                "IssaPlugin",
                "NumInventorySlots",
                6f,
                "Change the number of inventory slots available."
            );

            FirstPlaceStarEnabled = cfg.Bind(
                "IssaPlugin",
                "FirstPlaceStarEnabled",
                false,
                "Show a gold star above the first-place player so they are easy to spot."
            );

            FirstPlaceStarHeight = cfg.Bind(
                "IssaPlugin",
                "FirstPlaceStarHeight",
                1.5f,
                "Height in Unity units above the player's origin at which the gold star appears."
            );

            // ── Diagnostics ────────────────────────────────────────────────────
            NetworkDiagnosticsEnabled = cfg.Bind(
                "Diagnostics",
                "NetworkDiagnosticsEnabled",
                false,
                "Log a periodic summary of network traffic and latency to the BepInEx log. "
                    + "Enable this if you are reporting lag so the log shows which messages "
                    + "dominate bandwidth. Set to false for normal play."
            );

            NetworkDiagnosticsInterval = cfg.Bind(
                "Diagnostics",
                "NetworkDiagnosticsInterval",
                30f,
                "Seconds between network diagnostics summaries. Only used when "
                    + "NetworkDiagnosticsEnabled is true."
            );

            BomberOverlayEnabled = cfg.Bind(
                "Diagnostics",
                "BomberOverlayEnabled",
                true,
                "A/B TEST. Set to false to disable the stealth bomber / predator missile "
                    + "screen overlay (scanlines, vignette, noise, glitch bands) entirely. "
                    + "Purely cosmetic — use it to measure how much of the FPS drop during "
                    + "those items comes from the overlay."
            );

            PlayerBoxOverlayEnabled = cfg.Bind(
                "Diagnostics",
                "PlayerBoxOverlayEnabled",
                true,
                "A/B TEST. Set to false to disable the player target boxes and name "
                    + "labels drawn during stealth bomber targeting, predator missile "
                    + "steering, and AC130 sessions. Purely cosmetic — use it to measure "
                    + "how much of the FPS drop during those items comes from this overlay."
            );

            CustomVfxEnabled = cfg.Bind(
                "Diagnostics",
                "CustomVfxEnabled",
                true,
                "A/B TEST. Set to false to disable all custom particle and trail VFX "
                    + "(explosions, smoke/fire trails, Red Bull and Spinach trails, black "
                    + "hole, drone explosions, blood splatter, and similar). Item mechanics "
                    + "are unaffected — only the visuals are skipped. Use it to test whether "
                    + "the mod's VFX prefabs, and the shaders they were converted to for "
                    + "URP, are responsible for frame drops."
            );

            PerfDiagnosticsEnabled = cfg.Bind(
                "Diagnostics",
                "PerfDiagnosticsEnabled",
                false,
                "Log a periodic performance report: frame timing, CPU/GPU counters, GC "
                    + "allocation, a census of spawned network objects with per-type change "
                    + "since the last report, and physics object counts. Enable this if you "
                    + "are reporting FPS problems. Set to false for normal play."
            );

            ModCpuProfilingEnabled = cfg.Bind(
                "Diagnostics",
                "ModCpuProfilingEnabled",
                false,
                "Attribute main-thread CPU time to the mod's own subsystems (Harmony "
                    + "patches, overlays, network bridges) and report it alongside the "
                    + "performance report. Answers how much of the frame the mod itself "
                    + "costs. Adds a small timing overhead to every patch, so enable it "
                    + "only while investigating. REQUIRES A GAME RESTART to take effect, "
                    + "because the timing wrappers are installed at startup."
            );

            PerfDiagnosticsInterval = cfg.Bind(
                "Diagnostics",
                "PerfDiagnosticsInterval",
                30f,
                "Seconds between performance reports. Only used when "
                    + "PerfDiagnosticsEnabled is true."
            );

            PerfDiagnosticsTopObjects = cfg.Bind(
                "Diagnostics",
                "PerfDiagnosticsTopObjects",
                15,
                "How many of the most numerous spawned object types to list in each "
                    + "performance report."
            );

            NetworkDiagnosticsTopMessages = cfg.Bind(
                "Diagnostics",
                "NetworkDiagnosticsTopMessages",
                8,
                "How many of the heaviest message types to list in each diagnostics summary."
            );

            // ── UI ─────────────────────────────────────────────────────────────
            SpawnConfigUIKey = cfg.Bind(
                "UI",
                "SpawnConfigUIKey",
                Key.M,
                "Hotkey that opens/closes the Spawn Config GUI panel. "
                    + "The panel lets the host adjust per-pool spawn weights at runtime."
            );

            // ── ItemEnabled flags ──────────────────────────────────────────────
            static void Reg(
                ConfigFile c,
                Dictionary<int, ConfigEntry<bool>> map,
                int id,
                string key,
                string label
            ) =>
                map[id] = c.Bind(
                    "ItemEnabled",
                    key,
                    true,
                    $"Whether the {label} is enabled and can spawn."
                );

            Reg(cfg, _itemEnabledEntries, 100, "BaseballBatEnabled", "Baseball Bat");
            Reg(cfg, _itemEnabledEntries, 101, "StealthBomberEnabled", "Stealth Bomber");
            Reg(cfg, _itemEnabledEntries, 102, "PredatorMissileEnabled", "Predator Missile");
            Reg(cfg, _itemEnabledEntries, 103, "AC130Enabled", "AC-130 Gunship");
            Reg(cfg, _itemEnabledEntries, 104, "FreezeEnabled", "Freeze World");
            Reg(cfg, _itemEnabledEntries, 105, "LowGravityEnabled", "Low Gravity");
            Reg(cfg, _itemEnabledEntries, 106, "SniperRifleEnabled", "Sniper Rifle");
            Reg(cfg, _itemEnabledEntries, 107, "DonutEnabled", "Donut");
            Reg(cfg, _itemEnabledEntries, 108, "JavelinEnabled", "Javelin");
            Reg(cfg, _itemEnabledEntries, 109, "StickyGrenadeEnabled", "Sticky Grenade");
            Reg(cfg, _itemEnabledEntries, 110, "BearEnabled", "Bear");
            Reg(cfg, _itemEnabledEntries, 111, "NukeEnabled", "Nuke");
            Reg(cfg, _itemEnabledEntries, 112, "BlackHoleGrenadeEnabled", "Black Hole Grenade");
            Reg(cfg, _itemEnabledEntries, 113, "PlaceableWallEnabled", "Placeable Wall");
            Reg(cfg, _itemEnabledEntries, 114, "AK47Enabled", "AK-47");
            Reg(cfg, _itemEnabledEntries, 115, "HarrierEnabled", "Harrier Jet");
            Reg(cfg, _itemEnabledEntries, 116, "PositionSwapEnabled", "Position Swap");
            Reg(cfg, _itemEnabledEntries, 117, "PoisonJarEnabled", "Poison Jar");
            Reg(cfg, _itemEnabledEntries, 118, "DroneSwarmEnabled", "Drone Swarm");
            Reg(cfg, _itemEnabledEntries, 119, "RedBullEnabled", "Red Bull");
            Reg(cfg, _itemEnabledEntries, 120, "SuperDonutEnabled", "Super Donut");
            Reg(cfg, _itemEnabledEntries, 121, "GravityGunEnabled", "Gravity Gun");
            Reg(cfg, _itemEnabledEntries, 122, "RocketTetherEnabled", "Rocket Tether");
            Reg(cfg, _itemEnabledEntries, 123, "JetpackEnabled", "Jetpack");
            Reg(cfg, _itemEnabledEntries, 124, "TeleporterEnabled", "Teleporter");
            Reg(cfg, _itemEnabledEntries, 125, "SpinachEnabled", "Spinach");
            Reg(cfg, _itemEnabledEntries, 126, "FlamethrowerEnabled", "Flamethrower");
            Reg(
                cfg,
                _itemEnabledEntries,
                127,
                "RocketTetherGrenadeEnabled",
                "Rocket Tether Grenade"
            );

            // ── Warnings ───────────────────────────────────────────────────────
            WarningsEnabled = cfg.Bind(
                "Warnings",
                "Enabled",
                true,
                "Master toggle for all item-use warning banners and PiP cameras."
            );

            WarningDuration = cfg.Bind(
                "Warnings",
                "Duration",
                5.0f,
                "How long (seconds) each warning banner and PiP camera is displayed."
            );

            WarningShowForSelf = cfg.Bind(
                "Warnings",
                "ShowForSelf",
                false,
                "If true, the warning banner and PiP also appear for the player who used the item. Useful for testing."
            );

            WarningSymbolEnabled = cfg.Bind(
                "Warnings",
                "WarningSymbolEnabled",
                true,
                "If true, the warning particle effect is shown. Each client can disable it locally."
            );

            WarningPrefabScale = cfg.Bind(
                "Warnings",
                "WarningSymbolScale",
                0.75f,
                "Controls the size of the warning prefab."
            );

            WarningSymbolAlpha = cfg.Bind(
                "Warnings",
                "WarningSymbolAlpha",
                0.5f,
                "Controls the alpha channel of the start color of the warning prefab."
            );

            WarningPiPScale = cfg.Bind(
                "Warnings",
                "PiPScale",
                1.0f,
                "Scale multiplier for the PiP camera box (base size 320x180). "
                    + "Valid range: 0.5 (160x90) to 2.0 (640x360)."
            );

            // ── ItemWarnings per-item flags ────────────────────────────────────
            static void RegWarn(
                ConfigFile c,
                Dictionary<int, ConfigEntry<bool>> map,
                int id,
                string key,
                string label,
                bool defaultOn
            ) =>
                map[id] = c.Bind(
                    "ItemWarnings",
                    key,
                    defaultOn,
                    $"Show a warning to all other players when {label} is used."
                );

            RegWarn(
                cfg,
                _itemWarningEnabledEntries,
                100,
                "BaseballBatWarning",
                "Baseball Bat",
                false
            );
            RegWarn(cfg, _itemWarningEnabledEntries, 101, "BomberWarning", "Stealth Bomber", true);
            RegWarn(
                cfg,
                _itemWarningEnabledEntries,
                102,
                "MissileWarning",
                "Predator Missile",
                true
            );
            RegWarn(cfg, _itemWarningEnabledEntries, 103, "AC130Warning", "AC-130 Gunship", true);
            RegWarn(cfg, _itemWarningEnabledEntries, 104, "FreezeWarning", "Freeze World", false);
            RegWarn(
                cfg,
                _itemWarningEnabledEntries,
                105,
                "LowGravityWarning",
                "Low Gravity",
                false
            );
            RegWarn(
                cfg,
                _itemWarningEnabledEntries,
                106,
                "SniperRifleWarning",
                "Sniper Rifle",
                false
            );
            RegWarn(cfg, _itemWarningEnabledEntries, 107, "DonutWarning", "Donut", true);
            RegWarn(cfg, _itemWarningEnabledEntries, 108, "JavelinWarning", "Javelin", false);
            RegWarn(
                cfg,
                _itemWarningEnabledEntries,
                109,
                "StickyGrenadeWarning",
                "Sticky Grenade",
                false
            );
            RegWarn(cfg, _itemWarningEnabledEntries, 110, "BearWarning", "Bear", true);
            RegWarn(cfg, _itemWarningEnabledEntries, 111, "NukeWarning", "Nuke", true);
            RegWarn(
                cfg,
                _itemWarningEnabledEntries,
                112,
                "BlackHoleGrenadeWarning",
                "Black Hole Grenade",
                false
            );
            RegWarn(
                cfg,
                _itemWarningEnabledEntries,
                113,
                "PlaceableWallWarning",
                "Placeable Wall",
                false
            );
            RegWarn(cfg, _itemWarningEnabledEntries, 114, "AK47Warning", "AK-47", false);
            RegWarn(cfg, _itemWarningEnabledEntries, 115, "HarrierWarning", "Harrier Jet", true);
            RegWarn(
                cfg,
                _itemWarningEnabledEntries,
                116,
                "PositionSwapWarning",
                "Position Swap",
                false
            );
            RegWarn(cfg, _itemWarningEnabledEntries, 117, "PoisonJarWarning", "Poison Jar", false);
            RegWarn(cfg, _itemWarningEnabledEntries, 118, "DroneSwarmWarning", "Drone Swarm", true);
            RegWarn(cfg, _itemWarningEnabledEntries, 119, "RedBullWarning", "Red Bull", false);
            RegWarn(cfg, _itemWarningEnabledEntries, 120, "SuperDonutWarning", "Super Donut", true);
            RegWarn(
                cfg,
                _itemWarningEnabledEntries,
                121,
                "GravityGunWarning",
                "Gravity Gun",
                false
            );
            RegWarn(
                cfg,
                _itemWarningEnabledEntries,
                122,
                "RocketTetherWarning",
                "Rocket Tether",
                false
            );
            RegWarn(cfg, _itemWarningEnabledEntries, 123, "JetpackWarning", "Jetpack", false);
            RegWarn(cfg, _itemWarningEnabledEntries, 124, "TeleporterWarning", "Teleporter", false);
            RegWarn(cfg, _itemWarningEnabledEntries, 125, "SpinachWarning", "Spinach", false);
            RegWarn(
                cfg,
                _itemWarningEnabledEntries,
                126,
                "FlamethrowerWarning",
                "Flamethrower",
                false
            );
            RegWarn(
                cfg,
                _itemWarningEnabledEntries,
                127,
                "RocketTetherGrenadeWarning",
                "Rocket Tether Grenade",
                false
            );

            // GlobalConfig.BindAllItemPoolWeights binds all per-pool weights centrally.
            BindAllItemPoolWeights(cfg);
        }

        private void BindAllItemPoolWeights(ConfigFile cfg)
        {
            foreach (var def in ItemRegistry.AllItems)
            {
                foreach (var (pool, suffix) in PoolSuffixes)
                {
                    _poolWeightEntries[((int)def.ItemType, pool)] = cfg.Bind(
                        "ItemBoxSpawns",
                        $"{def.ConfigKeyPrefix}Weight{suffix}",
                        def.GetDefaultPoolWeight(pool),
                        $"Spawn weight for {def.DisplayName} in the '{suffix}' pool. "
                            + $"0 = never spawns in this pool."
                    );
                }
            }
        }
    }
}
