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
    static class SetEquippedItemPatch
    {
        static MethodBase TargetMethod() =>
            AccessTools.Method(typeof(PlayerAnimatorIo), "SetEquippedItem");

        static void Prefix(ref ItemType equippedItem)
        {
            if (ItemRegistry.IsCustomItem(equippedItem))
                equippedItem = ItemType.None;
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

            foreach (var poolData in __instance.ItemPools)
                InjectIntoPool(poolData.pool, customEntries);

            if (__instance.AheadOfBallItemPool != null)
                InjectIntoPool(__instance.AheadOfBallItemPool, customEntries);

            IssaPluginPlugin.Log.LogInfo(
                $"[ItemPool] Injected {customEntries.Length} custom items into item box pools."
            );
        }

        private static ItemPool.ItemSpawnChance[] BuildCustomEntries()
        {
            var list = new List<ItemPool.ItemSpawnChance>();

            float batWeight = Configuration.BaseballBatSpawnWeight.Value;
            if (batWeight > 0f)
                list.Add(
                    new ItemPool.ItemSpawnChance
                    {
                        item = BatItem.BatItemType,
                        spawnChanceWeight = batWeight,
                    }
                );

            float bomberWeight = Configuration.BomberSpawnWeight.Value;
            if (bomberWeight > 0f)
                list.Add(
                    new ItemPool.ItemSpawnChance
                    {
                        item = StealthBomberItem.BomberItemType,
                        spawnChanceWeight = bomberWeight,
                    }
                );

            float missileWeight = Configuration.MissileSpawnWeight.Value;
            if (missileWeight > 0f)
                list.Add(
                    new ItemPool.ItemSpawnChance
                    {
                        item = PredatorMissileItem.MissileItemType,
                        spawnChanceWeight = missileWeight,
                    }
                );

            return list.ToArray();
        }

        private static void InjectIntoPool(ItemPool pool, ItemPool.ItemSpawnChance[] customEntries)
        {
            if (pool == null)
                return;

            var existing = (ItemPool.ItemSpawnChance[])SpawnChancesField.GetValue(pool);
            if (existing == null)
                existing = Array.Empty<ItemPool.ItemSpawnChance>();

            var merged = new ItemPool.ItemSpawnChance[existing.Length + customEntries.Length];
            Array.Copy(existing, 0, merged, 0, existing.Length);
            Array.Copy(customEntries, 0, merged, existing.Length, customEntries.Length);

            SpawnChancesField.SetValue(pool, merged);

            float totalWeight = 0f;
            foreach (var entry in merged)
                totalWeight += entry.spawnChanceWeight;
            TotalWeightField.SetValue(pool, totalWeight);
        }
    }
}
