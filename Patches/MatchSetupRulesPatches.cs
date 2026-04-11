using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using IssaPlugin.Items;

namespace IssaPlugin.Patches
{
    /// <summary>
    /// Extends MatchSetupRules' weight queries to include custom items, whose weights
    /// are stored separately (cannot be added to spawnChanceWeights without crashing
    /// SpawnChanceUpdated's itemOrderLookup array access on custom ItemType values).
    ///
    /// EffectiveWeights is populated by ItemSpawnerResetRuntimeDataPatch and cleared
    /// before each full rebuild cycle (via ClearEffectiveWeightsPatch and
    /// SpawnWeightsSyncer.BroadcastWeightsIfChanged).
    /// </summary>
    // [HarmonyPatch] with no arguments on the outer class is required for PatchAll
    // to discover the method-level [HarmonyPatch] target attributes on each method.
    // Without it, all three patches are silently skipped by harmony.PatchAll(assembly).
    [HarmonyPatch]
    public static class MatchSetupRulesPatches
    {
        // (game pool index, ItemType) → effective weight injected into that pool.
        internal static readonly Dictionary<(int, ItemType), float> EffectiveWeights = new();

        // ── GetWeight ──────────────────────────────────────────────────────────
        [HarmonyPatch(typeof(MatchSetupRules), nameof(MatchSetupRules.GetWeight))]
        [HarmonyPostfix]
        static void GetWeightPatch(int poolIndex, ItemType itemType, ref float __result)
        {
            if (!ItemRegistry.IsCustomItem(itemType)) return;
            if (EffectiveWeights.TryGetValue((poolIndex, itemType), out float w))
                __result = w;
        }

        // ── GetItemPoolTotalWeight ─────────────────────────────────────────────
        [HarmonyPatch(typeof(MatchSetupRules), nameof(MatchSetupRules.GetItemPoolTotalWeight))]
        [HarmonyPostfix]
        static void GetItemPoolTotalWeightPatch(int index, ref float __result)
        {
            foreach (var kv in EffectiveWeights)
                if (kv.Key.Item1 == index)
                    __result += kv.Value;
        }

        // ── ResetSpawnChances — clear before full rebuild ──────────────────────
        // MatchSetupRules.ResetSpawnChances() calls ResetRuntimeData on both
        // spawners. Clear EffectiveWeights first so stale entries from disabled
        // items do not persist into the next cycle.
        [HarmonyPatch(typeof(MatchSetupRules), "ResetSpawnChances")]
        [HarmonyPrefix]
        static void ClearEffectiveWeightsPatch() => EffectiveWeights.Clear();
    }


    /// <summary>
    /// Prevents IndexOutOfRangeException in MatchSetupRules when custom items are present
    /// in item pools. The game's itemOrderLookup only covers vanilla ItemType values
    /// (Coffee=1 through OrbitalLaser=10). Our custom items start at 100, so any lookup
    /// via itemOrderLookup[item - ItemType.Coffee] crashes with an out-of-bounds index.
    ///
    /// Two crash sites:
    /// 1. SpawnChanceUpdated — fires when the Pro Golf preset zeroes out all spawn weights.
    /// 2. Update — iterates pool SpawnChances every frame to refresh the percentage labels.
    /// </summary>
    [HarmonyPatch]
    static class MatchSetupRulesSpawnChanceUpdatedPatch
    {
        static MethodBase TargetMethod() =>
            AccessTools.Method(
                typeof(MatchSetupRules),
                "SpawnChanceUpdated",
                new[] { typeof(MatchSetupRules.ItemPoolId) }
            );

        // Return false (skip base) for custom items — there are no UI sliders for them.
        // Harmony003 is a false positive: the analyzer misreads struct field reads as writes.
#pragma warning disable Harmony003
        static bool Prefix(MatchSetupRules.ItemPoolId itemPoolId) =>
            !ItemRegistry.IsCustomItem(itemPoolId.itemType);
#pragma warning restore Harmony003
    }

    [HarmonyPatch]
    static class MatchSetupRulesUpdatePatch
    {
        static MethodBase TargetMethod() => AccessTools.Method(typeof(MatchSetupRules), "Update");

        private static readonly FieldInfo CurrentItemPoolDirtyField = AccessTools.Field(
            typeof(MatchSetupRules),
            "currentItemPoolDirty"
        );
        private static readonly MethodInfo GetCurrentItemPoolMethod = AccessTools.Method(
            typeof(MatchSetupRules),
            "GetCurrentItemPool"
        );
        private static readonly FieldInfo CurrentItemPoolIndexField = AccessTools.Field(
            typeof(MatchSetupRules),
            "currentItemPoolIndex"
        );
        private static readonly FieldInfo SpawnChanceWeightsField = AccessTools.Field(
            typeof(MatchSetupRules),
            "spawnChanceWeights"
        );
        private static readonly FieldInfo SpawnChanceSlidersField = AccessTools.Field(
            typeof(MatchSetupRules),
            "spawnChanceSliders"
        );

        static bool Prefix(MatchSetupRules __instance)
        {
            if (!MatchSetupMenu.IsActive)
                return false;

            bool dirty = (bool)CurrentItemPoolDirtyField.GetValue(__instance);
            if (!dirty)
                return false;

            var currentPool = (ItemPool)GetCurrentItemPoolMethod.Invoke(__instance, null);
            int poolIndex = (int)CurrentItemPoolIndexField.GetValue(__instance);
            var weights =
                (IDictionary<MatchSetupRules.ItemPoolId, float>)
                    SpawnChanceWeightsField.GetValue(__instance);
            var sliders = (List<SliderOption>)SpawnChanceSlidersField.GetValue(__instance);
            int[] lookup = __instance.itemOrderLookup;

            float total = 0f;
            foreach (var chance in currentPool.SpawnChances)
            {
                if (ItemRegistry.IsCustomItem(chance.item))
                    continue;
                var key = MatchSetupRules.ItemPoolId.Get(poolIndex, chance.item);
                if (weights.ContainsKey(key))
                    total += weights[key];
            }

            foreach (var chance in currentPool.SpawnChances)
            {
                if (ItemRegistry.IsCustomItem(chance.item))
                    continue;
                int offset = (int)chance.item - (int)ItemType.Coffee;
                if (offset < 0 || offset >= lookup.Length)
                    continue;
                int sliderIdx = lookup[offset];
                if (sliderIdx < 0 || sliderIdx >= sliders.Count)
                    continue;
                var key = MatchSetupRules.ItemPoolId.Get(poolIndex, chance.item);
                float pct =
                    (total > float.Epsilon && weights.ContainsKey(key))
                        ? (weights[key] / total)
                        : 0f;
                sliders[sliderIdx].SetValueText(string.Format("{0:0.#}%", pct * 100f));
            }

            CurrentItemPoolDirtyField.SetValue(__instance, false);
            return false;
        }
    }
}
