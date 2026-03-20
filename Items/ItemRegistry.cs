using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using IssaPlugin.Items;
using IssaPlugin.Overlays;
using IssaPlugin.Patches;
using Mirror;
using UnityEngine;
using UnityEngine.InputSystem;

namespace IssaPlugin.Items
{
    public static class ItemRegistry
    {
        // Static initialization order note: AllItems is a static field initializer that only
        // instantiates the definition objects; it does not call any abstract members. Properties like
        // MaxUses, SpawnWeight, and GiveKey are evaluated lazily at call time, after Configuration
        // is fully initialized. So, there is no static initialization ordering risk.
        public static IReadOnlyList<CustomItemDefinition> AllItems { get; } =
            new List<CustomItemDefinition>
            {
                new BatItemDefinition(),
                new StealthBomberItemDefinition(),
                new PredatorMissileItemDefinition(),
                new AC130ItemDefinition(),
                new FreezeItemDefinition(),
                new LowGravityItemDefinition(),
                new SniperRifleItemDefinition(),
                new DonutItemDefinition(),
                new JavelinItemDefinition(),
                new StickyGrenadeItemDefinition(),
                new BearItemDefinition(),
            };

        public static CustomItemDefinition GetDefinition(ItemType type) =>
            AllItems.FirstOrDefault(d => d.ItemType == type);

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

        public static bool IsCustomItem(ItemType type) => AllItems.Any(d => d.ItemType == type);

        public static int GetMaxUses(ItemType type) => GetDefinition(type)?.MaxUses ?? 1;

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
            { /* log error */
                return;
            }

            // Resolve both fallback sprites before the loop.
            Sprite rocketFallbackIcon = dict.TryGetValue(ItemType.RocketLauncher, out var rd)
                ? rd.Icon
                : null;
            Sprite pistolFallbackIcon = dict.TryGetValue(ItemType.DuelingPistol, out var pd)
                ? pd.Icon
                : null;

            foreach (var def in AllItems)
            {
                var data = GetOrCreateItemData(def.ItemType);
                Sprite fallback = def.UseRocketIconFallback
                    ? rocketFallbackIcon
                    : pistolFallbackIcon;
                IconProperty.SetValue(data, def.Icon ?? fallback);
                dict[def.ItemType] = data;
            }

            IssaPluginPlugin.Log.LogInfo($"[ItemRegistry] Injected {AllItems.Count} custom items.");
        }

        /// Adds entries to the Unity Localization "Data" StringTable at runtime
        /// so that custom item names resolve correctly everywhere.
        /// Must be called after the game is fully initialized (not during ScriptableObject.OnEnable).
        /// Returns true if names were registered successfully, false if the localization
        /// table wasn't ready yet (caller should retry next frame).
        public static bool RegisterCustomItemNames()
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
                    return false;
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
                    return false;

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
                    return false;

                var table = getTableMethod.Invoke(stringDb, new[] { tableRef, null });
                if (table == null)
                {
                    IssaPluginPlugin.Log.LogWarning(
                        "[ItemRegistry] Data string table not loaded yet — will retry."
                    );
                    return false;
                }

                var addEntryMethod = table
                    .GetType()
                    .GetMethod("AddEntry", new[] { typeof(string), typeof(string) });
                if (addEntryMethod == null)
                    return false;

                addEntryMethod.Invoke(table, new object[] { "ITEM_100", "Baseball Bat" });
                addEntryMethod.Invoke(table, new object[] { "ITEM_101", "Stealth Bomber" });
                addEntryMethod.Invoke(table, new object[] { "ITEM_102", "Predator Missile" });
                addEntryMethod.Invoke(table, new object[] { "ITEM_103", "AC130 Gunship" });
                addEntryMethod.Invoke(table, new object[] { "ITEM_104", "Freeze World" });
                addEntryMethod.Invoke(table, new object[] { "ITEM_105", "Low Gravity" });
                addEntryMethod.Invoke(table, new object[] { "ITEM_106", "M200 Intervention" });
                addEntryMethod.Invoke(table, new object[] { "ITEM_107", "Donut" });
                addEntryMethod.Invoke(table, new object[] { "ITEM_108", "Javelin" });
                addEntryMethod.Invoke(table, new object[] { "ITEM_109", "StickyGrenade" });
                addEntryMethod.Invoke(table, new object[] { "ITEM_110", "Bear" });

                IssaPluginPlugin.Log.LogInfo(
                    "[ItemRegistry] Custom item names registered in string table."
                );
                return true;
            }
            catch (Exception e)
            {
                IssaPluginPlugin.Log.LogWarning(
                    $"[ItemRegistry] Failed to register item names: {e.Message}"
                );
                return false;
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
