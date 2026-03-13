using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Mirror;
using UnityEngine;

namespace IssaPlugin.Items
{
    public static class ItemRegistry
    {
        private static readonly FieldInfo SlotsField = AccessTools.Field(
            typeof(PlayerInventory),
            "slots"
        );

        private static readonly FieldInfo AllItemDataField = AccessTools.Field(
            typeof(ItemCollection),
            "allItemData"
        );

        private static readonly PropertyInfo IconProperty = AccessTools.Property(
            typeof(ItemData),
            "Icon"
        );

        private static readonly Dictionary<ItemType, ItemData> CustomItemDataCache =
            new Dictionary<ItemType, ItemData>();

        public static bool IsCustomItem(ItemType type)
        {
            return type == BatItem.BatItemType
                || type == StealthBomberItem.BomberItemType
                || type == PredatorMissileItem.MissileItemType
                || type == AC130Item.AC130ItemType
                || type == FreezeItem.FreezeItemType
                || type == LowGravityItem.LowGravityItemType
                || type == SniperRifleItem.SniperRifleItemType;
        }

        public static int GetMaxUses(ItemType type)
        {
            if (type == BatItem.BatItemType)
                return (int)Configuration.BaseballBatUses.Value;
            if (type == StealthBomberItem.BomberItemType)
                return (int)Configuration.BomberUses.Value;
            if (type == PredatorMissileItem.MissileItemType)
                return (int)Configuration.MissileUses.Value;
            if (type == AC130Item.AC130ItemType)
                return (int)Configuration.AC130Uses.Value;
            if (type == FreezeItem.FreezeItemType)
                return (int)Configuration.FreezeUses.Value;
            if (type == LowGravityItem.LowGravityItemType)
                return (int)Configuration.LowGravityUses.Value;
            if (type == SniperRifleItem.SniperRifleItemType)
                return (int)Configuration.SniperRifleUses.Value;
            return 1;
        }

        internal static ItemData GetOrCreateItemData(ItemType type)
        {
            if (CustomItemDataCache.TryGetValue(type, out var cached))
            {
                AccessTools
                    .Property(typeof(ItemData), "MaxUses")
                    .SetValue(cached, GetMaxUses(type));
                return cached;
            }

            var data = new ItemData();
            var t = typeof(ItemData);
            AccessTools.Property(t, "Type").SetValue(data, type);
            AccessTools.Property(t, "MaxUses").SetValue(data, GetMaxUses(type));
            AccessTools.Property(t, "Icon").SetValue(data, null);
            AccessTools.Property(t, "Prefab").SetValue(data, null);
            AccessTools.Property(t, "AnimatorOverrideController").SetValue(data, null);
            AccessTools.Property(t, "IsExplosive").SetValue(data, false);
            AccessTools.Property(t, "NonAimUse").SetValue(data, ItemNonAimingUse.None);
            AccessTools.Property(t, "AirhornReaction").SetValue(data, ItemAirhornReaction.None);
            AccessTools.Property(t, "CanUsageAffectBalls").SetValue(data, false);
            AccessTools.Property(t, "HitTransfersToGolfCartPassengers").SetValue(data, false);
            AccessTools.Property(t, "FlourishFrames").SetValue(data, 0f);
            AccessTools.Property(t, "ConsumptionEffectStartTime").SetValue(data, 0f);
            AccessTools.Property(t, "PostConsumptionEffectStartTime").SetValue(data, 0f);
            AccessTools.Property(t, "DroppedLocalRotationEuler").SetValue(data, Vector3.zero);
            data.Initialize();

            CustomItemDataCache[type] = data;
            return data;
        }

        internal static void InjectCustomItems(ItemCollection collection)
        {
            var dict = (Dictionary<ItemType, ItemData>)AllItemDataField.GetValue(collection);
            if (dict == null)
            {
                IssaPluginPlugin.Log.LogError("[ItemRegistry] allItemData field is null!");
                return;
            }

            var batData = GetOrCreateItemData(BatItem.BatItemType);
            var bomberData = GetOrCreateItemData(StealthBomberItem.BomberItemType);
            var missileData = GetOrCreateItemData(PredatorMissileItem.MissileItemType);
            var ac130Data = GetOrCreateItemData(AC130Item.AC130ItemType);
            var freezeData = GetOrCreateItemData(FreezeItem.FreezeItemType);
            var lowGravityData = GetOrCreateItemData(LowGravityItem.LowGravityItemType);
            var sniperRifleData = GetOrCreateItemData(SniperRifleItem.SniperRifleItemType);

            Sprite rocketFallbackIcon = null;
            if (
                dict.TryGetValue(ItemType.RocketLauncher, out var rocketData)
                && rocketData.Icon != null
            )
                rocketFallbackIcon = rocketData.Icon;

            Sprite pistolFallbackIcon = null;
            if (
                dict.TryGetValue(ItemType.DuelingPistol, out var pistolData)
                && pistolData.Icon != null
            )
                pistolFallbackIcon = pistolData.Icon;

            IconProperty.SetValue(batData, AssetLoader.BatIcon ?? pistolFallbackIcon);
            IconProperty.SetValue(bomberData, AssetLoader.BomberIcon ?? rocketFallbackIcon);
            IconProperty.SetValue(missileData, AssetLoader.MissileIcon ?? rocketFallbackIcon);
            IconProperty.SetValue(ac130Data, AssetLoader.AC130Icon ?? rocketFallbackIcon);
            IconProperty.SetValue(freezeData, AssetLoader.FreezeIcon ?? rocketFallbackIcon);
            IconProperty.SetValue(lowGravityData, AssetLoader.LowGravityIcon ?? rocketFallbackIcon);
            IconProperty.SetValue(
                sniperRifleData,
                AssetLoader.SniperRifleIcon ?? pistolFallbackIcon
            );

            dict[BatItem.BatItemType] = batData;
            dict[StealthBomberItem.BomberItemType] = bomberData;
            dict[PredatorMissileItem.MissileItemType] = missileData;
            dict[AC130Item.AC130ItemType] = ac130Data;
            dict[FreezeItem.FreezeItemType] = freezeData;
            dict[LowGravityItem.LowGravityItemType] = lowGravityData;
            dict[SniperRifleItem.SniperRifleItemType] = sniperRifleData;

            IssaPluginPlugin.Log.LogInfo(
                $"[ItemRegistry] Injected {CustomItemDataCache.Count} custom items."
            );
        }

        /// Adds entries to the Unity Localization "Data" StringTable at runtime
        /// so that custom item names resolve correctly everywhere.
        /// Must be called after the game is fully initialized (not during ScriptableObject.OnEnable).
        public static void RegisterCustomItemNames()
        {
            try
            {
                Assembly locAsm = null;
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (asm.GetName().Name == "Unity.Localization")
                    {
                        locAsm = asm;
                        break;
                    }
                }
                if (locAsm == null)
                {
                    IssaPluginPlugin.Log.LogWarning(
                        "[ItemRegistry] Unity.Localization assembly not found."
                    );
                    return;
                }

                var locSettingsType = locAsm.GetType(
                    "UnityEngine.Localization.Settings.LocalizationSettings"
                );
                var stringDbProp = locSettingsType.GetProperty(
                    "StringDatabase",
                    BindingFlags.Public | BindingFlags.Static
                );
                var stringDb = stringDbProp.GetValue(null);
                if (stringDb == null)
                    return;

                var tableRefType = locAsm.GetType("UnityEngine.Localization.Tables.TableReference");
                var implicitOp = tableRefType.GetMethod(
                    "op_Implicit",
                    BindingFlags.Public | BindingFlags.Static,
                    null,
                    new[] { typeof(string) },
                    null
                );
                var tableRef = implicitOp.Invoke(null, new object[] { "Data" });

                MethodInfo getTableMethod = null;
                foreach (
                    var m in stringDb
                        .GetType()
                        .GetMethods(BindingFlags.Public | BindingFlags.Instance)
                )
                {
                    if (m.Name != "GetTable")
                        continue;
                    var pars = m.GetParameters();
                    if (pars.Length == 2 && pars[0].ParameterType == tableRefType)
                    {
                        getTableMethod = m;
                        break;
                    }
                }
                if (getTableMethod == null)
                    return;

                var table = getTableMethod.Invoke(stringDb, new[] { tableRef, null });
                if (table == null)
                {
                    IssaPluginPlugin.Log.LogWarning(
                        "[ItemRegistry] Data string table not loaded yet."
                    );
                    return;
                }

                var addEntryMethod = table
                    .GetType()
                    .GetMethod("AddEntry", new[] { typeof(string), typeof(string) });
                if (addEntryMethod == null)
                    return;

                addEntryMethod.Invoke(table, new object[] { "ITEM_100", "Baseball Bat" });
                addEntryMethod.Invoke(table, new object[] { "ITEM_101", "Stealth Bomber" });
                addEntryMethod.Invoke(table, new object[] { "ITEM_102", "Predator Missile" });
                addEntryMethod.Invoke(table, new object[] { "ITEM_103", "AC130 Gunship" });
                addEntryMethod.Invoke(table, new object[] { "ITEM_104", "Freeze World" });
                addEntryMethod.Invoke(table, new object[] { "ITEM_105", "Low Gravity" });
                addEntryMethod.Invoke(table, new object[] { "ITEM_106", "M200 Intervention" });

                IssaPluginPlugin.Log.LogInfo(
                    "[ItemRegistry] Custom item names registered in string table."
                );
            }
            catch (Exception e)
            {
                IssaPluginPlugin.Log.LogWarning(
                    $"[ItemRegistry] Failed to register item names: {e.Message}"
                );
            }
        }

        public static bool DirectAddCustomItem(
            PlayerInventory inventory,
            ItemType itemType,
            int uses
        )
        {
            if (!NetworkServer.active)
                return false;

            int emptyIndex;
            if (!inventory.HasSpaceForItem(out emptyIndex))
            {
                IssaPluginPlugin.Log.LogWarning("[ItemRegistry] No empty slot available.");
                return false;
            }

            var slots = (IList<InventorySlot>)SlotsField.GetValue(inventory);
            slots[emptyIndex] = new InventorySlot(itemType, uses > 0 ? uses : 1);

            IssaPluginPlugin.Log.LogInfo(
                $"[ItemRegistry] Added custom item {(int)itemType} to slot {emptyIndex} ({uses} uses)."
            );
            return true;
        }
    }
}
