using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using IssaPlugin.Items;

namespace IssaPlugin.Patches
{
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
