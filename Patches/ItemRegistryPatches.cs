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

    [HarmonyPatch]
    static class ItemSpawnerResetRuntimeDataPatch
    {
        // Use the same target as before.
        static MethodBase TargetMethod() =>
            AccessTools.Method(typeof(ItemSpawnerSettings), "ResetRuntimeData");

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
            var pools = __instance.ItemPools;
            int poolCount = pools.Count;
            float boostFactor = Configuration.CatchupBoostFactor.Value;

            for (int poolIndex = 0; poolIndex < poolCount; poolIndex++)
            {
                // Approximate "place" as pool index + 1 (pool 0 = 1st place proxy).
                int approxPlace = poolIndex + 1;
                // Approximate distance-behind-leader using the same linear scale as
                // the catchup multiplier; gives a rough-but-consistent proxy.
                float approxDist = poolCount > 1 ? (float)poolIndex / (poolCount - 1) * 500f : 0f;

                float t = poolCount > 1 ? (float)poolIndex / (poolCount - 1) : 0f;
                float multiplier = 1f + boostFactor * t;

                var entries = BuildCustomEntries(approxDist, approxPlace);
                if (entries.Length > 0)
                    InjectIntoPool(pools[poolIndex].pool, entries, multiplier);
            }

            if (__instance.AheadOfBallItemPool != null)
            {
                var entries = BuildCustomEntries(0f, 1);
                if (entries.Length > 0)
                    InjectIntoPool(__instance.AheadOfBallItemPool, entries, 1f);
            }

            IssaPluginPlugin.Log.LogInfo(
                $"[ItemPool] Injected custom items into {poolCount} pools (gating applied)."
            );
        }

        // ── Entry builder with tier gating ────────────────────────────────────

        private static ItemPool.ItemSpawnChance[] BuildCustomEntries(
            float approxDist,
            int approxPlace
        )
        {
            if (!Configuration.CustomItemSpawnsEnabled.Value)
                return Array.Empty<ItemPool.ItemSpawnChance>();

            float rate = Configuration.CustomItemSpawnRate.Value;
            if (rate <= 0f)
                return Array.Empty<ItemPool.ItemSpawnChance>();

            var syncer = TierConfigSyncer.Instance;

            var list = new List<ItemPool.ItemSpawnChance>();

            foreach (var def in ItemRegistry.AllItems)
            {
                if (!def.Enabled || def.SpawnWeight <= 0f)
                    continue;

                // ── Tier-level gating ─────────────────────────────────────────
                bool tierAllowed =
                    syncer == null
                    || syncer.IsTierAllowedForPlayer(def.Tier, approxDist, approxPlace);

                if (!tierAllowed)
                    continue;

                list.Add(
                    new ItemPool.ItemSpawnChance
                    {
                        item = def.ItemType,
                        spawnChanceWeight = def.SpawnWeight * rate,
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

            var existing =
                (ItemPool.ItemSpawnChance[])SpawnChancesField.GetValue(pool)
                ?? Array.Empty<ItemPool.ItemSpawnChance>();

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
            foreach (var e in merged)
                totalWeight += e.spawnChanceWeight;
            TotalWeightField.SetValue(pool, totalWeight);
        }
    }
}
