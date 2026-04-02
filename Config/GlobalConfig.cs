using System.Collections.Generic;
using BepInEx.Configuration;
using IssaPlugin.Items;
using UnityEngine;
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

        // ── ItemBoxSpawns section ─────────────────────────────────────────────
        public ConfigEntry<float> CatchupBoostFactor { get; private set; }
        public ConfigEntry<int> NumTiers { get; private set; }

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

        // ── Per-item tier assignments ─────────────────────────────────────────
        private readonly Dictionary<int, ConfigEntry<int>> _itemTierEntries = new();

        public int GetItemTier(ItemType itemType)
        {
            if (_itemTierEntries.TryGetValue((int)itemType, out var entry))
                return Mathf.Clamp(entry.Value, 1, Mathf.Max(1, NumTiers?.Value ?? 3));
            var def = ItemRegistry.GetDefinition(itemType);
            return def?.Tier ?? 1;
        }

        public void SetItemTier(ItemType itemType, int tier)
        {
            if (_itemTierEntries.TryGetValue((int)itemType, out var entry))
                entry.Value = Mathf.Clamp(tier, 1, Mathf.Max(1, NumTiers?.Value ?? ModConfig.MaxTiers));
        }

        // ── Per-item spawn weight overrides ───────────────────────────────────
        private readonly Dictionary<int, ConfigEntry<float>> _itemSpawnWeightOverrideValueEntries = new();
        private readonly Dictionary<int, ConfigEntry<bool>> _itemSpawnWeightOverrideEnabledEntries = new();

        public bool GetItemSpawnWeightOverrideEnabled(ItemType t) =>
            _itemSpawnWeightOverrideEnabledEntries.TryGetValue((int)t, out var e) && e.Value;

        public float GetItemSpawnWeightOverrideValue(ItemType t) =>
            _itemSpawnWeightOverrideValueEntries.TryGetValue((int)t, out var e) ? e.Value : 0f;

        public void SetItemSpawnWeightOverride(ItemType itemType, bool enabled, float value)
        {
            int key = (int)itemType;
            if (_itemSpawnWeightOverrideEnabledEntries.TryGetValue(key, out var enabledEntry))
                enabledEntry.Value = enabled;
            if (_itemSpawnWeightOverrideValueEntries.TryGetValue(key, out var valueEntry))
                valueEntry.Value = value;
        }

        // ── Per-tier spawn weights ────────────────────────────────────────────
        private readonly Dictionary<int, ConfigEntry<float>> _tierSpawnWeightEntries = new();
        private readonly Dictionary<int, ConfigEntry<float>> _tierMinDistanceEntries = new();
        private readonly Dictionary<int, ConfigEntry<int>> _tierMinPlaceEntries = new();

        public float GetTierSpawnWeight(int tier)
        {
            if (_tierSpawnWeightEntries.TryGetValue(tier, out var entry))
                return entry.Value;
            IssaPluginPlugin.Log.LogWarning(
                $"[Config] Item tier {tier} has no registered spawn weight; falling back to Tier 2."
            );
            return _tierSpawnWeightEntries.TryGetValue(2, out var fallback) ? fallback.Value : 8f;
        }

        public void SetTierSpawnWeight(int tier, float value)
        {
            if (_tierSpawnWeightEntries.TryGetValue(tier, out var entry))
                entry.Value = value;
        }

        public float GetTierMinDistance(int tier) =>
            _tierMinDistanceEntries.TryGetValue(tier, out var e) ? e.Value : 0f;

        public void SetTierMinDistance(int tier, float value)
        {
            if (_tierMinDistanceEntries.TryGetValue(tier, out var entry))
                entry.Value = value;
        }

        public int GetTierMinPlace(int tier) =>
            _tierMinPlaceEntries.TryGetValue(tier, out var e) ? e.Value : 0;

        public void SetTierMinPlace(int tier, int value)
        {
            if (_tierMinPlaceEntries.TryGetValue(tier, out var entry))
                entry.Value = value;
        }

        // ── Per-item warning flags ────────────────────────────────────────────
        private readonly Dictionary<int, ConfigEntry<bool>> _itemWarningEnabledEntries = new();

        public bool GetItemWarningEnabled(ItemType itemType) =>
            WarningsEnabled.Value
            && (_itemWarningEnabledEntries.TryGetValue((int)itemType, out var e) ? e.Value : false);

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

            // ── ItemBoxSpawns ──────────────────────────────────────────────────
            CatchupBoostFactor = cfg.Bind(
                "ItemBoxSpawns",
                "CatchupBoostFactor",
                2f,
                "Multiplies custom item spawn weights for players further behind the leader. "
                    + "At 0, every item box pool has equal custom item chances. "
                    + "At 1 (default), the pool for the furthest-behind player gets 2× the weight of the closest pool. "
                    + "At 2 it gets 3×, etc. Set to 0 to disable catchup scaling."
            );

            NumTiers = cfg.Bind("ItemBoxSpawns", "NumTiers", 5, "Number of item tiers (1–10). ...");

            float[] defaultWeights = { 15f, 10f, 5f, 3f, 1f, 1f, 1f, 1f, 1f, 1f };
            int numTiers = Mathf.Clamp(NumTiers.Value, 1, ModConfig.MaxTiers);
            for (int t = 1; t <= numTiers; t++)
            {
                float def = t <= defaultWeights.Length ? defaultWeights[t - 1] : 1f;
                BindTierWeight(cfg, t, def, $"Spawn weight for Tier {t} items.");
            }

            // ── UI ─────────────────────────────────────────────────────────────
            SpawnConfigUIKey = cfg.Bind(
                "UI",
                "SpawnConfigUIKey",
                Key.M,
                "Hotkey that opens/closes the Spawn Config GUI panel. "
                    + "The panel lets the host adjust tier weights, per-item overrides, "
                    + "and distance/place gating at runtime."
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

            RegWarn(cfg, _itemWarningEnabledEntries, 100, "BaseballBatWarning", "Baseball Bat", false);
            RegWarn(cfg, _itemWarningEnabledEntries, 101, "BomberWarning", "Stealth Bomber", true);
            RegWarn(cfg, _itemWarningEnabledEntries, 102, "MissileWarning", "Predator Missile", true);
            RegWarn(cfg, _itemWarningEnabledEntries, 103, "AC130Warning", "AC-130 Gunship", true);
            RegWarn(cfg, _itemWarningEnabledEntries, 104, "FreezeWarning", "Freeze World", false);
            RegWarn(cfg, _itemWarningEnabledEntries, 105, "LowGravityWarning", "Low Gravity", false);
            RegWarn(cfg, _itemWarningEnabledEntries, 106, "SniperRifleWarning", "Sniper Rifle", false);
            RegWarn(cfg, _itemWarningEnabledEntries, 107, "DonutWarning", "Donut", true);
            RegWarn(cfg, _itemWarningEnabledEntries, 108, "JavelinWarning", "Javelin", false);
            RegWarn(cfg, _itemWarningEnabledEntries, 109, "StickyGrenadeWarning", "Sticky Grenade", false);
            RegWarn(cfg, _itemWarningEnabledEntries, 110, "BearWarning", "Bear", true);
            RegWarn(cfg, _itemWarningEnabledEntries, 111, "NukeWarning", "Nuke", true);
            RegWarn(cfg, _itemWarningEnabledEntries, 112, "BlackHoleGrenadeWarning", "Black Hole Grenade", false);
            RegWarn(cfg, _itemWarningEnabledEntries, 113, "PlaceableWallWarning", "Placeable Wall", false);
            RegWarn(cfg, _itemWarningEnabledEntries, 114, "AK47Warning", "AK-47", false);
            RegWarn(cfg, _itemWarningEnabledEntries, 115, "HarrierWarning", "Harrier Jet", true);
            RegWarn(cfg, _itemWarningEnabledEntries, 116, "PositionSwapWarning", "Position Swap", false);
            RegWarn(cfg, _itemWarningEnabledEntries, 117, "PoisonJarWarning", "Poison Jar", false);
            RegWarn(cfg, _itemWarningEnabledEntries, 118, "DroneSwarmWarning", "Drone Swarm", true);

            // ── ItemTierAssignments ────────────────────────────────────────────
            BindAllItemTierAssignments(cfg);
        }

        internal ConfigEntry<float> BindSpawnWeight(
            ConfigFile cfg,
            int itemTypeId,
            string key,
            float defaultValue,
            string valueDesc
        )
        {
            var valueEntry = cfg.Bind(
                "ItemBoxSpawns",
                key,
                defaultValue,
                $"{valueDesc} Only active when {key}Override is true."
            );
            var overrideEntry = cfg.Bind(
                "ItemBoxSpawns",
                key + "Override",
                false,
                $"When true, use {key} directly instead of the tier's spawn weight."
            );
            _itemSpawnWeightOverrideValueEntries[itemTypeId] = valueEntry;
            _itemSpawnWeightOverrideEnabledEntries[itemTypeId] = overrideEntry;
            return valueEntry;
        }

        private void BindTierWeight(ConfigFile cfg, int tier, float defaultValue, string description)
        {
            _tierSpawnWeightEntries[tier] = cfg.Bind(
                "ItemBoxSpawns",
                $"Tier{tier}Weight",
                defaultValue,
                description
            );
            _tierMinDistanceEntries[tier] = cfg.Bind(
                "ItemBoxSpawns",
                $"Tier{tier}MinDistanceBehindLeader",
                0f,
                $"Min distance behind leader required to unlock Tier {tier} items (0 = no gate)."
            );
            _tierMinPlaceEntries[tier] = cfg.Bind(
                "ItemBoxSpawns",
                $"Tier{tier}MinPlaceTrigger",
                0,
                $"Min place (e.g. 3 = 3rd or worse) required to unlock Tier {tier} items (0 = no gate)."
            );
        }

        private void BindAllItemTierAssignments(ConfigFile cfg)
        {
            foreach (var def in ItemRegistry.AllItems)
            {
                int id = (int)def.ItemType;
                _itemTierEntries[id] = cfg.Bind(
                    "ItemTierAssignments",
                    def.DisplayName,
                    def.Tier,
                    $"Tier assignment for {def.DisplayName} (item ID {id}). "
                        + $"Must be between 1 and NumTiers ({NumTiers?.Value ?? 3})."
                );
            }
        }
    }
}
