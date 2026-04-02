// TierConfig.cs
// Holds all runtime-mutable spawn configuration that the SpawnConfigUI can edit.
// The host owns the authoritative copy; clients receive it via TierConfigMessage.
//
// IMPORTANT: This is intentionally kept separate from BepInEx ConfigFile entries.
// The host always writes changes back to Configuration (and therefore to the .cfg
// file), so they persist across sessions. Clients receive a live snapshot for
// cosmetic display only; they never write to their own .cfg files from here.

using System.Collections.Generic;
using IssaPlugin.Items;
using UnityEngine;

namespace IssaPlugin
{
    // ─── Per-Tier Settings ────────────────────────────────────────────────────

    public class TierSettings
    {
        public int Tier;
        public float SpawnWeight;
        public bool TierEnabled;
        public float MinDistanceBehindLeader;
        public int MinPlaceTrigger;

        public TierSettings(int tier, float spawnWeight)
        {
            Tier = tier;
            SpawnWeight = spawnWeight;
            TierEnabled = true;
            MinDistanceBehindLeader = 0f;
            MinPlaceTrigger = 0;
        }

        public TierSettings(TierSettings src)
        {
            Tier = src.Tier;
            SpawnWeight = src.SpawnWeight;
            TierEnabled = src.TierEnabled;
            MinDistanceBehindLeader = src.MinDistanceBehindLeader;
            MinPlaceTrigger = src.MinPlaceTrigger;
        }
    }

    // ─── Per-Item Override ────────────────────────────────────────────────────

    public class ItemOverrideSettings
    {
        public int ItemTypeId;

        /// <summary>
        /// The tier this item currently belongs to.
        /// Loaded from Configuration.GetItemTier() and written back via SetItemTier().
        /// Defaults to CustomItemDefinition.Tier if no config entry exists yet.
        /// </summary>
        public int AssignedTier;

        public bool Enabled;
        public bool SpawnWeightOverrideEnabled;
        public float SpawnWeightOverride;

        public ItemOverrideSettings(int itemTypeId)
        {
            ItemTypeId = itemTypeId;
            AssignedTier = Configuration.GetItemTier((ItemType)itemTypeId);
            Enabled = Configuration.GetItemEnabled((ItemType)itemTypeId);
            SpawnWeightOverrideEnabled = Configuration.GetItemSpawnWeightOverrideEnabled(
                (ItemType)itemTypeId
            );
            SpawnWeightOverride = Configuration.GetItemSpawnWeightOverrideValue(
                (ItemType)itemTypeId
            );
        }

        // Wire-construction: caller sets all fields via object initializer.
        // Does not read from Configuration — use when unpacking a received network message.
        internal ItemOverrideSettings(int itemTypeId, bool _)
        {
            ItemTypeId = itemTypeId;
        }

        public ItemOverrideSettings(ItemOverrideSettings src)
        {
            ItemTypeId = src.ItemTypeId;
            AssignedTier = src.AssignedTier;
            Enabled = src.Enabled;
            SpawnWeightOverrideEnabled = src.SpawnWeightOverrideEnabled;
            SpawnWeightOverride = src.SpawnWeightOverride;
        }
    }

    // ─── Master Config Snapshot ───────────────────────────────────────────────

    public class SpawnConfigSnapshot
    {
        public bool CustomItemSpawnsEnabled;
        public float GlobalSpawnRateMultiplier;
        public float CatchupBoostFactor;

        // Keyed by tier number (1 … NumTiers)
        public Dictionary<int, TierSettings> TierSettings = new();

        // Keyed by (int)ItemType
        public Dictionary<int, ItemOverrideSettings> ItemOverrides = new();

        // ── Build from config ─────────────────────────────────────────────────

        public static SpawnConfigSnapshot FromConfiguration()
        {
            var snap = new SpawnConfigSnapshot
            {
                CustomItemSpawnsEnabled = Configuration.CustomItemSpawnsEnabled.Value,
                GlobalSpawnRateMultiplier = Configuration.CustomItemSpawnRate.Value,
                CatchupBoostFactor = Configuration.CatchupBoostFactor.Value,
            };

            int numTiers =
                Configuration.NumTiers != null
                    ? Mathf.Clamp(Configuration.NumTiers.Value, 1, Configuration.MaxTiers)
                    : 3;
            for (int t = 1; t <= numTiers; t++)
                snap.TierSettings[t] = new TierSettings(t, Configuration.GetTierSpawnWeight(t))
                {
                    MinDistanceBehindLeader = Configuration.GetTierMinDistance(t),
                    MinPlaceTrigger = Configuration.GetTierMinPlace(t),
                };

            foreach (var def in ItemRegistry.AllItems)
            {
                int id = (int)def.ItemType;
                snap.ItemOverrides[id] = new ItemOverrideSettings(id);
            }

            return snap;
        }

        // ── Deep copy ─────────────────────────────────────────────────────────

        public SpawnConfigSnapshot DeepCopy()
        {
            var copy = new SpawnConfigSnapshot
            {
                CustomItemSpawnsEnabled = CustomItemSpawnsEnabled,
                GlobalSpawnRateMultiplier = GlobalSpawnRateMultiplier,
                CatchupBoostFactor = CatchupBoostFactor,
            };
            foreach (var kv in TierSettings)
                copy.TierSettings[kv.Key] = new TierSettings(kv.Value);
            foreach (var kv in ItemOverrides)
                copy.ItemOverrides[kv.Key] = new ItemOverrideSettings(kv.Value);
            return copy;
        }

        // ── Write back to config ──────────────────────────────────────────────

        /// <summary>
        /// Persists this snapshot to BepInEx config entries and triggers a pool rebuild + sync.
        /// Host-only.
        /// </summary>
        public void ApplyToConfiguration()
        {
            Configuration.CustomItemSpawnsEnabled.Value = CustomItemSpawnsEnabled;
            Configuration.CustomItemSpawnRate.Value = GlobalSpawnRateMultiplier;
            Configuration.CatchupBoostFactor.Value = CatchupBoostFactor;

            // NumTiers — write the count so BindTierWeight loop is accurate on next startup.
            int highestUsedTier = 1;
            foreach (var kv in TierSettings)
                if (kv.Key > highestUsedTier)
                    highestUsedTier = kv.Key;
            Configuration.NumTiers.Value = highestUsedTier;

            // Tier weights and gating
            foreach (var kv in TierSettings)
            {
                Configuration.SetTierSpawnWeight(kv.Key, kv.Value.SpawnWeight);
                Configuration.SetTierMinDistance(kv.Key, kv.Value.MinDistanceBehindLeader);
                Configuration.SetTierMinPlace(kv.Key, kv.Value.MinPlaceTrigger);
            }

            // Per-item overrides + tier assignments
            foreach (var kv in ItemOverrides)
            {
                var ov = kv.Value;
                var type = (ItemType)ov.ItemTypeId;
                Configuration.SetItemEnabled(type, ov.Enabled);
                Configuration.SetItemSpawnWeightOverride(
                    type,
                    ov.SpawnWeightOverrideEnabled,
                    ov.SpawnWeightOverride
                );
                Configuration.SetItemTier(type, ov.AssignedTier); // ← the new persistent write
            }

            SpawnWeightsSyncer.ForceServerSync();
        }
    }
}
