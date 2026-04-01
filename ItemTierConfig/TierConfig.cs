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

namespace IssaPlugin
{
    // ─── Per-Tier Settings ────────────────────────────────────────────────────

    /// <summary>
    /// All configurable settings that apply to one item tier.
    /// Passed over the network inside <see cref="TierConfigMessage"/>.
    /// </summary>
    public class TierSettings
    {
        public int Tier;

        // ── Spawn weight ──────────────────────────────────────────────────────
        /// <summary>Base spawn-weight for items in this tier (relative to other tiers).</summary>
        public float SpawnWeight;

        // ── Enable / disable ──────────────────────────────────────────────────
        /// <summary>
        /// When false the entire tier is excluded from every item pool.
        /// Individual item Enabled flags are still respected within an enabled tier.
        /// </summary>
        public bool TierEnabled;

        // ── Distance gating (optional) ───────────────────────────────────────
        /// <summary>
        /// If > 0, items in this tier can only spawn for a player who is at least
        /// <see cref="MinDistanceBehindLeader"/> units behind the player currently
        /// closest to the hole.
        /// Set to 0 (or negative) to disable distance gating.
        /// </summary>
        public float MinDistanceBehindLeader;

        // ── Place/score gating (optional) ─────────────────────────────────────
        /// <summary>
        /// If > 0, items in this tier only spawn for a player who is in place
        /// <see cref="MinPlaceTrigger"/> or worse (higher number = further back).
        /// E.g. MinPlaceTrigger = 3 means items only appear for 3rd-place or below.
        /// Set to 0 to disable.
        /// </summary>
        public int MinPlaceTrigger;

        public TierSettings(int tier, float spawnWeight)
        {
            Tier            = tier;
            SpawnWeight     = spawnWeight;
            TierEnabled     = true;
            MinDistanceBehindLeader = 0f;
            MinPlaceTrigger = 0;
        }

        /// Deep-copy constructor used when broadcasting to avoid aliasing.
        public TierSettings(TierSettings src)
        {
            Tier                    = src.Tier;
            SpawnWeight             = src.SpawnWeight;
            TierEnabled             = src.TierEnabled;
            MinDistanceBehindLeader = src.MinDistanceBehindLeader;
            MinPlaceTrigger         = src.MinPlaceTrigger;
        }
    }

    // ─── Per-Item Override ────────────────────────────────────────────────────

    /// <summary>
    /// Per-item overrides that the UI can set independently of tier defaults.
    /// </summary>
    public class ItemOverrideSettings
    {
        public int ItemTypeId;

        /// <summary>Whether this item is allowed to spawn at all.</summary>
        public bool Enabled;

        /// <summary>Whether the per-item spawn-weight override is active.</summary>
        public bool SpawnWeightOverrideEnabled;

        /// <summary>The override spawn weight (used only when <see cref="SpawnWeightOverrideEnabled"/> is true).</summary>
        public float SpawnWeightOverride;

        public ItemOverrideSettings(int itemTypeId)
        {
            ItemTypeId                 = itemTypeId;
            Enabled                    = Configuration.GetItemEnabled((ItemType)itemTypeId);
            SpawnWeightOverrideEnabled = Configuration.GetItemSpawnWeightOverrideEnabled((ItemType)itemTypeId);
            SpawnWeightOverride        = Configuration.GetItemSpawnWeightOverrideValue((ItemType)itemTypeId);
        }

        public ItemOverrideSettings(ItemOverrideSettings src)
        {
            ItemTypeId                 = src.ItemTypeId;
            Enabled                    = src.Enabled;
            SpawnWeightOverrideEnabled = src.SpawnWeightOverrideEnabled;
            SpawnWeightOverride        = src.SpawnWeightOverride;
        }
    }

    // ─── Master Config Snapshot ───────────────────────────────────────────────

    /// <summary>
    /// A complete snapshot of all spawn-config state.  The host holds one authoritative
    /// instance; <see cref="TierConfigSyncer"/> broadcasts it to clients on change.
    /// </summary>
    public class SpawnConfigSnapshot
    {
        public bool CustomItemSpawnsEnabled;
        public float GlobalSpawnRateMultiplier;
        public float CatchupBoostFactor;

        // Keyed by tier number (1, 2, 3, …)
        public Dictionary<int, TierSettings> TierSettings = new Dictionary<int, TierSettings>();

        // Keyed by (int)ItemType
        public Dictionary<int, ItemOverrideSettings> ItemOverrides = new Dictionary<int, ItemOverrideSettings>();

        /// <summary>
        /// Build a snapshot from the current BepInEx configuration values.
        /// Call this on the host only.
        /// </summary>
        public static SpawnConfigSnapshot FromConfiguration()
        {
            var snap = new SpawnConfigSnapshot
            {
                CustomItemSpawnsEnabled    = Configuration.CustomItemSpawnsEnabled.Value,
                GlobalSpawnRateMultiplier  = Configuration.CustomItemSpawnRate.Value,
                CatchupBoostFactor         = Configuration.CatchupBoostFactor.Value,
            };

            // Discover all tiers used by registered items.
            var tiersSeen = new HashSet<int>();
            foreach (var def in ItemRegistry.AllItems)
                tiersSeen.Add(def.Tier);

            foreach (int tier in tiersSeen)
            {
                snap.TierSettings[tier] = new TierSettings(tier, Configuration.GetTierSpawnWeight(tier));
            }

            foreach (var def in ItemRegistry.AllItems)
            {
                int id = (int)def.ItemType;
                snap.ItemOverrides[id] = new ItemOverrideSettings(id);
            }

            return snap;
        }

        /// Deep-copy.
        public SpawnConfigSnapshot DeepCopy()
        {
            var copy = new SpawnConfigSnapshot
            {
                CustomItemSpawnsEnabled   = CustomItemSpawnsEnabled,
                GlobalSpawnRateMultiplier = GlobalSpawnRateMultiplier,
                CatchupBoostFactor        = CatchupBoostFactor,
            };
            foreach (var kv in TierSettings)
                copy.TierSettings[kv.Key] = new TierSettings(kv.Value);
            foreach (var kv in ItemOverrides)
                copy.ItemOverrides[kv.Key] = new ItemOverrideSettings(kv.Value);
            return copy;
        }

        /// <summary>
        /// Write a snapshot back to Configuration (host side only).
        /// Triggers <see cref="SpawnWeightsSyncer.ForceServerSync"/> so changes
        /// propagate to all clients immediately.
        /// </summary>
        public void ApplyToConfiguration()
        {
            Configuration.CustomItemSpawnsEnabled.Value   = CustomItemSpawnsEnabled;
            Configuration.CustomItemSpawnRate.Value       = GlobalSpawnRateMultiplier;
            Configuration.CatchupBoostFactor.Value        = CatchupBoostFactor;

            // Per-item overrides
            foreach (var kv in ItemOverrides)
            {
                var ov = kv.Value;
                Configuration.SetItemEnabled((ItemType)ov.ItemTypeId, ov.Enabled);
                // SpawnWeightOverrideEnabled + SpawnWeightOverride are written via
                // the internal ConfigEntry dictionaries. We expose those through
                // Configuration helpers that must exist (or we add them below).
                Configuration.SetItemSpawnWeightOverride(
                    (ItemType)ov.ItemTypeId,
                    ov.SpawnWeightOverrideEnabled,
                    ov.SpawnWeightOverride);
            }

            // Tier weights are written back via Configuration helpers too.
            foreach (var kv in TierSettings)
                Configuration.SetTierSpawnWeight(kv.Key, kv.Value.SpawnWeight);

            SpawnWeightsSyncer.ForceServerSync();
        }
    }
}
