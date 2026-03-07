using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using IssaPlugin.Items;
using Mirror;
using UnityEngine;

namespace IssaPlugin.Patches
{
    public static class InventoryPatches
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
            return type == BatItem.BatItemType || type == StealthBomberItem.BomberItemType;
        }

        public static int GetMaxUses(ItemType type)
        {
            if (type == BatItem.BatItemType)
                return Configuration.BaseballBatUses.Value;
            if (type == StealthBomberItem.BomberItemType)
                return Configuration.BomberUses.Value;
            return 1;
        }

        private static ItemData GetOrCreateItemData(ItemType type)
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

        private static void InjectCustomItems(ItemCollection collection)
        {
            var dict = (Dictionary<ItemType, ItemData>)AllItemDataField.GetValue(collection);
            if (dict == null)
            {
                IssaPluginPlugin.Log.LogError("[Inventory] allItemData field is null!");
                return;
            }

            var batData = GetOrCreateItemData(BatItem.BatItemType);
            var bomberData = GetOrCreateItemData(StealthBomberItem.BomberItemType);

            // Borrow icons from existing game items
            if (
                dict.TryGetValue(ItemType.DuelingPistol, out var pistolData)
                && pistolData.Icon != null
            )
                IconProperty.SetValue(batData, pistolData.Icon);

            if (
                dict.TryGetValue(ItemType.RocketLauncher, out var rocketData)
                && rocketData.Icon != null
            )
                IconProperty.SetValue(bomberData, rocketData.Icon);

            dict[BatItem.BatItemType] = batData;
            dict[StealthBomberItem.BomberItemType] = bomberData;

            IssaPluginPlugin.Log.LogInfo(
                $"[Inventory] Injected {CustomItemDataCache.Count} custom items into ItemCollection."
            );
        }

        /// <summary>
        /// Adds entries to the Unity Localization "Data" StringTable at runtime
        /// so that our custom item names resolve correctly everywhere.
        /// Uses pure reflection to avoid needing a compile-time reference to Unity.Localization.dll.
        /// Must be called after the game is fully initialized (not during ScriptableObject.OnEnable).
        /// </summary>
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
                        "[Inventory] Unity.Localization assembly not found."
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

                // Build a TableReference from the string "Data"
                var tableRefType = locAsm.GetType("UnityEngine.Localization.Tables.TableReference");
                var implicitOp = tableRefType.GetMethod(
                    "op_Implicit",
                    BindingFlags.Public | BindingFlags.Static,
                    null,
                    new[] { typeof(string) },
                    null
                );
                var tableRef = implicitOp.Invoke(null, new object[] { "Data" });

                // Find GetTable(TableReference, Locale) on the StringDatabase
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
                        "[Inventory] Data string table not loaded yet; names will be set on next init."
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

                IssaPluginPlugin.Log.LogInfo(
                    "[Inventory] Custom item names registered in string table."
                );
            }
            catch (Exception e)
            {
                IssaPluginPlugin.Log.LogWarning(
                    $"[Inventory] Failed to register item names: {e.Message}"
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
                IssaPluginPlugin.Log.LogWarning("[Inventory] No empty slot available.");
                return false;
            }

            var slots = (IList<InventorySlot>)SlotsField.GetValue(inventory);
            slots[emptyIndex] = new InventorySlot(itemType, uses > 0 ? uses : 1);

            IssaPluginPlugin.Log.LogInfo(
                $"[Inventory] Added custom item {(int)itemType} to slot {emptyIndex} ({uses} uses)."
            );
            return true;
        }

        // ================================================================
        //  Inject custom items into the game's item registry.
        // ================================================================

        [HarmonyPatch]
        static class ItemCollectionInitPatch
        {
            static MethodBase TargetMethod() =>
                AccessTools.Method(typeof(ItemCollection), "Initialize");

            static void Postfix(ItemCollection __instance)
            {
                InjectCustomItems(__instance);
            }
        }

        // ================================================================
        //  Equipment display — bomber only (bat is handled automatically
        //  via GetEffectivelyEquippedItem returning None → GolfClub)
        // ================================================================

        [HarmonyPatch]
        static class UpdateEquipmentSwitchersPatch
        {
            static MethodBase TargetMethod() =>
                AccessTools.Method(typeof(PlayerInventory), "UpdateEquipmentSwitchers");

            static void Postfix(PlayerInventory __instance)
            {
                var equipped = __instance.GetEffectivelyEquippedItem(true);
                if (equipped == StealthBomberItem.BomberItemType)
                {
                    __instance.PlayerInfo.RightHandEquipmentSwitcher.SetEquipment(
                        EquipmentType.RocketLauncher
                    );
                    __instance.PlayerInfo.LeftHandEquipmentSwitcher.SetEquipment(
                        EquipmentType.None
                    );
                }
            }
        }

        // ================================================================
        //  Make the bat invisible to the equipped-item check so the
        //  golf swing system treats it as "no item" (golf club).
        // ================================================================

        [HarmonyPatch]
        static class GetEffectivelyEquippedItemPatch
        {
            static MethodBase TargetMethod() =>
                AccessTools.Method(typeof(PlayerInventory), "GetEffectivelyEquippedItem");

            static void Postfix(
                PlayerInventory __instance,
                bool ignoreEquipmentHiding,
                ref ItemType __result
            )
            {
                if (!ignoreEquipmentHiding && __result == BatItem.BatItemType)
                    __result = ItemType.None;
            }
        }

        // ================================================================
        //  Power multiplier — boost HitWithGolfSwing when bat is equipped
        // ================================================================

        [HarmonyPatch]
        static class HitWithGolfSwingPatch
        {
            static MethodBase TargetMethod() =>
                AccessTools.Method(typeof(Hittable), "HitWithGolfSwing");

            static void Prefix(PlayerGolfer hitter, ref float power)
            {
                if (hitter == null)
                    return;

                var inventory = hitter.PlayerInfo.Inventory;
                if (inventory.GetEffectivelyEquippedItem(true) == BatItem.BatItemType)
                    power *= Configuration.BaseballBatPowerMultiplier.Value;
            }
        }

        // ================================================================
        //  Decrement bat uses after each golf swing
        // ================================================================

        [HarmonyPatch]
        static class OnFinishedSwingingPatch
        {
            private static readonly MethodInfo DecrementMethod = AccessTools.Method(
                typeof(PlayerInventory),
                "DecrementUseFromSlotAt"
            );

            private static readonly MethodInfo RemoveMethod = AccessTools.Method(
                typeof(PlayerInventory),
                "RemoveIfOutOfUses"
            );

            static MethodBase TargetMethod() =>
                AccessTools.Method(typeof(PlayerGolfer), "OnFinishedSwinging");

            static void Postfix(PlayerGolfer __instance)
            {
                if (!__instance.isLocalPlayer)
                    return;

                var inventory = __instance.PlayerInfo.Inventory;
                if (inventory.GetEffectivelyEquippedItem(true) != BatItem.BatItemType)
                    return;

                int slotIndex = inventory.EquippedItemIndex;
                if (slotIndex < 0)
                    return;

                DecrementMethod.Invoke(inventory, new object[] { slotIndex });
                RemoveMethod.Invoke(inventory, new object[] { slotIndex });
            }
        }

        // ================================================================
        //  Item use — intercept for custom items (bomber only;
        //  bat uses the native golf swing system)
        // ================================================================

        [HarmonyPatch]
        static class TryUseItemPatch
        {
            static MethodBase TargetMethod() =>
                AccessTools.Method(typeof(PlayerInventory), "TryUseItem");

            static bool Prefix(
                PlayerInventory __instance,
                bool isAirhornReaction,
                ref bool shouldEatInput,
                ref bool __result
            )
            {
                var equipped = __instance.GetEffectivelyEquippedItem(true);

                if (equipped == BatItem.BatItemType)
                {
                    shouldEatInput = false;
                    __result = false;
                    return false;
                }

                if (equipped == StealthBomberItem.BomberItemType)
                {
                    shouldEatInput = true;
                    __result = true;
                    __instance.StartCoroutine(StealthBomberItem.BomberRunRoutine(__instance));
                    return false;
                }

                return true;
            }
        }

        // ================================================================
        //  Animator — default animation for custom items
        // ================================================================

        [HarmonyPatch]
        static class SetEquippedItemPatch
        {
            static MethodBase TargetMethod() =>
                AccessTools.Method(typeof(PlayerAnimatorIo), "SetEquippedItem");

            static void Prefix(ref ItemType equippedItem)
            {
                if (IsCustomItem(equippedItem))
                    equippedItem = ItemType.None;
            }
        }

        // ================================================================
        //  Server — bypass ServerTryAddItem for custom types
        // ================================================================

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
                if (!IsCustomItem(itemToAdd))
                    return true;

                __result = DirectAddCustomItem(__instance, itemToAdd, remainingUses);
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
                if (!IsCustomItem(item))
                    return true;

                DirectAddCustomItem(__instance, item, GetMaxUses(item));
                return false;
            }
        }

        // ================================================================
        //  Prevent dropping custom items (no physical prefabs)
        // ================================================================

        [HarmonyPatch]
        static class DropItemPatch
        {
            static MethodBase TargetMethod() =>
                AccessTools.Method(typeof(PlayerInventory), "DropItem");

            static bool Prefix(PlayerInventory __instance)
            {
                return !IsCustomItem(__instance.GetEffectivelyEquippedItem(false));
            }
        }
    }
}
