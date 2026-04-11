using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using IssaPlugin.Items;

namespace IssaPlugin.Patches
{
    /// <summary>
    /// Short-circuits ItemData.Name for custom items so the name is returned directly
    /// from CustomItemDefinition.DisplayName instead of going through Unity Localization.
    /// This avoids the Unity 6 async localization table issue that causes custom item
    /// names to display as "DATA_ITEM{id}" on all clients.
    /// </summary>
    [HarmonyPatch(typeof(ItemData), nameof(ItemData.Name), MethodType.Getter)]
    static class ItemDataNamePatch
    {
        static bool Prefix(ItemData __instance, ref string __result)
        {
            var def = ItemRegistry.GetDefinition(__instance.Type);
            if (def == null)
                return true; // not a custom item — run base game logic
            __result = def.DisplayName;
            return false;
        }
    }

    [HarmonyPatch]
    static class ItemCollectionInitPatch
    {
        static MethodBase TargetMethod() =>
            AccessTools.Method(typeof(ItemCollection), "Initialize");

        static void Postfix(ItemCollection __instance)
        {
            ItemRegistry.InjectCustomItems(__instance);
        }
    }

    [HarmonyPatch]
    static class ServerTryAddItemPatch
    {
        static MethodBase TargetMethod() =>
            AccessTools.Method(typeof(PlayerInventory), "ServerTryAddItem");

        static bool Prefix(
            PlayerInventory __instance,
            ItemType itemToAdd,
            int remainingUses,
            ref bool __result
        )
        {
            if (!ItemRegistry.IsCustomItem(itemToAdd))
                return true;

            __result = ItemRegistry.DirectAddCustomItem(__instance, itemToAdd, remainingUses);
            return false;
        }
    }

    [HarmonyPatch]
    static class CmdAddItemPatch
    {
        static MethodBase TargetMethod() =>
            AccessTools.Method(typeof(PlayerInventory), "UserCode_CmdAddItem__ItemType");

        static bool Prefix(PlayerInventory __instance, ItemType item)
        {
            if (!ItemRegistry.IsCustomItem(item))
                return true;

            ItemRegistry.DirectAddCustomItem(__instance, item, ItemRegistry.GetMaxUses(item));
            return false;
        }
    }

    [HarmonyPatch]
    static class ItemSpawnerResetRuntimeDataPatch
    {
        static MethodBase TargetMethod() =>
            AccessTools.Method(typeof(ItemSpawnerSettings), "ResetRuntimeData");

        private static readonly FieldInfo SpawnChancesField =
            AccessTools.Field(typeof(ItemPool), "spawnChances");
        private static readonly FieldInfo TotalWeightField =
            AccessTools.Field(typeof(ItemPool), "totalSpawnChanceWeight");

        static void Postfix(ItemSpawnerSettings __instance)
        {
            // Distinguish the regular spawner (pools 0-4) from the mobility spawner
            // (pool 5) by the presence of AheadOfBallItemPool. This is a stable
            // ScriptableObject inspector reference and requires no runtime reflection
            // on MatchSetupRules.
            bool isMobility = __instance.AheadOfBallItemPool == null;

            if (isMobility)
            {
                // Pool 5 — mobility item boxes
                if (__instance.ItemPools.Count > 0)
                    InjectPool(__instance.ItemPools[0].pool, GlobalConfig.PoolMobility);
            }
            else
            {
                // Pool 0 — ahead of own ball
                InjectPool(__instance.AheadOfBallItemPool, GlobalConfig.PoolAhead);

                // Pools 1-4 — distance-based (local index i → game index i+1)
                for (int i = 0; i < __instance.ItemPools.Count; i++)
                    InjectPool(__instance.ItemPools[i].pool, i + 1);
            }

            IssaPluginPlugin.Log.LogInfo(
                $"[ItemPool] Custom items injected into {(isMobility ? "mobility" : "regular")} spawner."
            );
        }

        private static void InjectPool(ItemPool pool, int gamePoolIndex)
        {
            if (pool == null) return;
            if (!ModConfig.Global.CustomItemSpawnsEnabled.Value) return;

            float rate = ModConfig.Global.CustomItemSpawnRate.Value;
            if (rate <= 0f) return;

            var toAdd = new List<ItemPool.ItemSpawnChance>();

            foreach (var def in ItemRegistry.AllItems)
            {
                if (!def.Enabled) continue;
                float w = def.GetPoolWeight(gamePoolIndex) * rate;
                if (w <= 0f) continue;

                toAdd.Add(new ItemPool.ItemSpawnChance
                {
                    item = def.ItemType,
                    spawnChanceWeight = w,
                });
                MatchSetupRulesPatches.EffectiveWeights[(gamePoolIndex, def.ItemType)] = w;
            }

            if (toAdd.Count == 0) return;

            var existing = (ItemPool.ItemSpawnChance[])SpawnChancesField.GetValue(pool)
                ?? Array.Empty<ItemPool.ItemSpawnChance>();

            var merged = new ItemPool.ItemSpawnChance[existing.Length + toAdd.Count];
            Array.Copy(existing, merged, existing.Length);
            for (int i = 0; i < toAdd.Count; i++)
                merged[existing.Length + i] = toAdd[i];

            SpawnChancesField.SetValue(pool, merged);

            float total = 0f;
            foreach (var e in merged) total += e.spawnChanceWeight;
            TotalWeightField.SetValue(pool, total);
        }
    }
}
