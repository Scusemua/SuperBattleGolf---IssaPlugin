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

        /// <summary>
        /// Injects custom ItemData into an ItemCollection's internal dictionary.
        /// Call after the collection is initialized (or from the Initialize postfix).
        /// </summary>
        private static void InjectCustomItems(ItemCollection collection)
        {
            var dict = (Dictionary<ItemType, ItemData>)AllItemDataField.GetValue(collection);
            if (dict == null)
            {
                IssaPluginPlugin.Log.LogError("[Inventory] allItemData field is null!");
                return;
            }

            dict[BatItem.BatItemType] = GetOrCreateItemData(BatItem.BatItemType);
            dict[StealthBomberItem.BomberItemType] = GetOrCreateItemData(
                StealthBomberItem.BomberItemType
            );

            IssaPluginPlugin.Log.LogInfo(
                $"[Inventory] Injected {CustomItemDataCache.Count} custom items into ItemCollection (dict now has {dict.Count} entries)."
            );
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
        //  ItemCollection.Initialize() clears & rebuilds allItemData;
        //  our postfix adds custom entries so every TryGetItemData /
        //  GetItemIcon / direct-dictionary lookup just works.
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
        //  Equipment display
        // ================================================================

        [HarmonyPatch]
        static class UpdateEquipmentSwitchersPatch
        {
            static MethodBase TargetMethod() =>
                AccessTools.Method(typeof(PlayerInventory), "UpdateEquipmentSwitchers");

            static void Postfix(PlayerInventory __instance)
            {
                var equipped = __instance.GetEffectivelyEquippedItem(false);
                if (equipped == BatItem.BatItemType)
                {
                    __instance.PlayerInfo.RightHandEquipmentSwitcher.SetEquipment(
                        EquipmentType.GolfClub
                    );
                    __instance.PlayerInfo.LeftHandEquipmentSwitcher.SetEquipment(
                        EquipmentType.None
                    );
                }
                else if (equipped == StealthBomberItem.BomberItemType)
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
        //  Item use — intercept for custom items
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
                var equipped = __instance.GetEffectivelyEquippedItem(false);

                if (equipped == BatItem.BatItemType)
                {
                    shouldEatInput = true;
                    __result = true;
                    __instance.StartCoroutine(BatItem.BatSwingRoutine(__instance));
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
