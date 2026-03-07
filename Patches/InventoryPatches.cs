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
            return type == BatItem.BatItemType
                || type == StealthBomberItem.BomberItemType
                || type == PredatorMissileItem.MissileItemType;
        }

        public static int GetMaxUses(ItemType type)
        {
            if (type == BatItem.BatItemType)
                return Configuration.BaseballBatUses.Value;
            if (type == StealthBomberItem.BomberItemType)
                return Configuration.BomberUses.Value;
            if (type == PredatorMissileItem.MissileItemType)
                return Configuration.MissileUses.Value;
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
            var missileData = GetOrCreateItemData(PredatorMissileItem.MissileItemType);

            if (
                dict.TryGetValue(ItemType.DuelingPistol, out var pistolData)
                && pistolData.Icon != null
            )
                IconProperty.SetValue(batData, pistolData.Icon);

            if (
                dict.TryGetValue(ItemType.RocketLauncher, out var rocketData)
                && rocketData.Icon != null
            )
            {
                IconProperty.SetValue(bomberData, rocketData.Icon);
                IconProperty.SetValue(missileData, rocketData.Icon);
            }

            dict[BatItem.BatItemType] = batData;
            dict[StealthBomberItem.BomberItemType] = bomberData;
            dict[PredatorMissileItem.MissileItemType] = missileData;

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
                addEntryMethod.Invoke(table, new object[] { "ITEM_102", "Predator Missile" });

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
                if (
                    equipped == StealthBomberItem.BomberItemType
                    || equipped == PredatorMissileItem.MissileItemType
                )
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
        //  Power multiplier — amplify the velocity delta from each hit
        //  instead of modifying the normalized power input, so we don't
        //  accidentally trigger BecomeSwingProjectile at lower charge.
        // ================================================================

        [HarmonyPatch]
        static class HitWithGolfSwingInternalPatch
        {
            internal static bool BatActive;
            private static Vector3 _velocityBefore;
            private static Vector3 _angularVelocityBefore;

            static MethodBase TargetMethod() =>
                AccessTools.Method(typeof(Hittable), "HitWithGolfSwingInternal");

            static void Prefix(Hittable __instance, PlayerGolfer hitter)
            {
                BatActive = false;
                if (hitter == null)
                    return;

                var inv = hitter.PlayerInfo.Inventory;
                if (inv.GetEffectivelyEquippedItem(true) != BatItem.BatItemType)
                    return;

                BatActive = true;
                if (__instance.AsEntity.HasRigidbody)
                {
                    _velocityBefore = __instance.AsEntity.Rigidbody.linearVelocity;
                    _angularVelocityBefore = __instance.AsEntity.Rigidbody.angularVelocity;
                }
            }

            static void Postfix(Hittable __instance)
            {
                if (!BatActive)
                    return;

                BatItem.PlayHomerunSound(__instance.transform.position);

                if (!__instance.AsEntity.HasRigidbody)
                    return;

                float extra = Configuration.BaseballBatPowerMultiplier.Value - 1f;
                if (extra <= 0f)
                    return;

                var rb = __instance.AsEntity.Rigidbody;
                rb.linearVelocity += (rb.linearVelocity - _velocityBefore) * extra;
                rb.angularVelocity += (rb.angularVelocity - _angularVelocityBefore) * extra;
            }
        }

        // ================================================================
        //  Skip BecomeSwingProjectile for bat hits — the projectile
        //  system's post-hit bounce resets velocity, which cancels
        //  out the bat's power boost at high charge levels.
        // ================================================================

        [HarmonyPatch]
        static class BecomeSwingProjectilePatch
        {
            static MethodBase TargetMethod() =>
                AccessTools.Method(typeof(Hittable), "BecomeSwingProjectile");

            static bool Prefix()
            {
                return !HitWithGolfSwingInternalPatch.BatActive;
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

                if (equipped == PredatorMissileItem.MissileItemType)
                {
                    shouldEatInput = true;
                    __result = true;
                    __instance.StartCoroutine(PredatorMissileItem.MissileRoutine(__instance));
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
        //  Predator missile: override rocket Start() velocity and
        //  reset the distance counter so it doesn't auto-explode.
        // ================================================================

        [HarmonyPatch(typeof(Rocket), "Start")]
        static class RocketStartPatch
        {
            static void Postfix(Rocket __instance)
            {
                if (PredatorMissileItem.ActiveMissileRocket != __instance)
                    return;

                var entity = __instance.GetComponent<Entity>();
                if (entity != null && entity.HasRigidbody)
                {
                    entity.Rigidbody.linearVelocity =
                        Vector3.down * Configuration.MissileFallSpeed.Value;
                }
            }
        }

        [HarmonyPatch(typeof(Rocket), "OnFixedBUpdate")]
        static class RocketFixedBUpdatePatch
        {
            private static readonly FieldInfo DistanceField = AccessTools.Field(
                typeof(Rocket),
                "distanceTravelled"
            );

            static void Prefix(Rocket __instance)
            {
                if (PredatorMissileItem.ActiveMissileRocket != __instance)
                    return;

                DistanceField?.SetValue(__instance, 0f);
            }
        }

        // ================================================================
        //  Custom items have no physical prefab, so instead of spawning
        //  a dropped object, just remove them from the inventory.
        // ================================================================

        [HarmonyPatch]
        static class DropItemPatch
        {
            private static readonly MethodInfo RemoveItemAtMethod = AccessTools.Method(
                typeof(PlayerInventory),
                "RemoveItemAt"
            );

            static MethodBase TargetMethod() =>
                AccessTools.Method(typeof(PlayerInventory), "DropItem");

            static bool Prefix(PlayerInventory __instance)
            {
                if (!IsCustomItem(__instance.GetEffectivelyEquippedItem(true)))
                    return true;

                int index = __instance.EquippedItemIndex;
                if (index < 0)
                    return false;

                RemoveItemAtMethod.Invoke(__instance, new object[] { index, false });
                return false;
            }
        }

        // ================================================================
        //  Inject custom items into the item box spawn pools so they
        //  appear naturally when players walk through item boxes.
        // ================================================================

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

            private static void InjectIntoPool(
                ItemPool pool,
                ItemPool.ItemSpawnChance[] customEntries
            )
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
}
