using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using IssaPlugin.Items;

namespace IssaPlugin.Patches
{
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

    [HarmonyPatch(typeof(ItemSpawnerSettings), "ResetRuntimeData")]
    static class ItemSpawnerResetRuntimeDataPatch
    {
        private static readonly FieldInfo SpawnChancesField = AccessTools.Field(
            typeof(ItemPool),
            "spawnChances"
        );

        private static readonly FieldInfo TotalWeightField = AccessTools.Field(
            typeof(ItemPool),
            "totalSpawnChanceWeight"
        );

        static void Postfix(ItemSpawnerSettings __instance)
        {
            var customEntries = BuildCustomEntries();
            if (customEntries.Length == 0)
                return;

            var pools = __instance.ItemPools;
            int n = pools.Count;
            float boostFactor = Configuration.CatchupBoostFactor.Value;

            for (int i = 0; i < n; i++)
            {
                // Pool 0 = closest to leader (no boost), last pool = furthest behind (max boost).
                float t = n > 1 ? (float)i / (n - 1) : 0f;
                float multiplier = 1f + boostFactor * t;
                InjectIntoPool(pools[i].pool, customEntries, multiplier);
            }

            if (__instance.AheadOfBallItemPool != null)
                InjectIntoPool(__instance.AheadOfBallItemPool, customEntries, 1f);

            IssaPluginPlugin.Log.LogInfo(
                $"[ItemPool] Injected {customEntries.Length} custom items into item box pools."
            );
        }

        private static ItemPool.ItemSpawnChance[] BuildCustomEntries()
        {
            if (!Configuration.CustomItemSpawnsEnabled.Value)
                return [];

            float rate = Configuration.CustomItemSpawnRate.Value;
            if (rate <= 0f)
                return [];

            var list = new List<ItemPool.ItemSpawnChance>();
            foreach (var itemDefinition in ItemRegistry.AllItems)
            {
                if (itemDefinition.Enabled && itemDefinition.SpawnWeight > 0f)
                    list.Add(
                        new ItemPool.ItemSpawnChance
                        {
                            item = itemDefinition.ItemType,
                            spawnChanceWeight = itemDefinition.SpawnWeight * rate,
                        }
                    );
            }
            return list.ToArray();
        }

        private static void InjectIntoPool(
            ItemPool pool,
            ItemPool.ItemSpawnChance[] customEntries,
            float multiplier
        )
        {
            if (pool == null)
                return;

            var existing = (ItemPool.ItemSpawnChance[])SpawnChancesField.GetValue(pool);
            if (existing == null)
                existing = Array.Empty<ItemPool.ItemSpawnChance>();

            var merged = new ItemPool.ItemSpawnChance[existing.Length + customEntries.Length];
            Array.Copy(existing, 0, merged, 0, existing.Length);
            for (int i = 0; i < customEntries.Length; i++)
                merged[existing.Length + i] = new ItemPool.ItemSpawnChance
                {
                    item = customEntries[i].item,
                    spawnChanceWeight = customEntries[i].spawnChanceWeight * multiplier,
                };

            SpawnChancesField.SetValue(pool, merged);

            float totalWeight = 0f;
            foreach (var entry in merged)
                totalWeight += entry.spawnChanceWeight;
            TotalWeightField.SetValue(pool, totalWeight);
        }
    }
}
