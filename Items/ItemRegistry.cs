using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using IssaPlugin.Items;
using Mirror;
using UnityEngine;

namespace IssaPlugin.Items
{
    public static class ItemRegistry
    {
        public static readonly ItemType BaseballBatItemType = (ItemType)100;
        public static readonly ItemType StealthBomberItemType = (ItemType)101;
        public static readonly ItemType PredatorMissileItemType = (ItemType)102;
        public static readonly ItemType AC130ItemType = (ItemType)103;

        public static readonly ItemType FreezeItemType = (ItemType)104;
        public static readonly ItemType LowGravityItemType = (ItemType)105;
        public static readonly ItemType SniperRifleItemType = (ItemType)106;
        public static readonly ItemType DonutItemType = (ItemType)107;
        public static readonly ItemType JavelinItemType = (ItemType)108;
        public static readonly ItemType StickyGrenadeItemType = (ItemType)109;
        public static readonly ItemType BearItemType = (ItemType)110;
        public static readonly ItemType NukeItemType = (ItemType)111;
        public static readonly ItemType BlackHoleGrenadeItemType = (ItemType)112;
        public static readonly ItemType PlaceableWallItemType = (ItemType)113;
        public static readonly ItemType AK47ItemType = (ItemType)114;
        public static readonly ItemType HarrierItemType = (ItemType)115;
        public static readonly ItemType PositionSwapItemType = (ItemType)116;
        public static readonly ItemType PoisonJarItemType = (ItemType)117;
        public static readonly ItemType DroneSwarmItemType = (ItemType)118;
        public static readonly ItemType RedBullItemType = (ItemType)119;

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
                new NukeItemDefinition(),
                new BlackHoleGrenadeItemDefinition(),
                new PlaceableWallItemDefinition(),
                new AK47ItemDefinition(),
                new HarrierItemDefinition(),
                new PositionSwapItemDefinition(),
                new PoisonJarItemDefinition(),
                new DroneSwarmItemDefinition(),
                new RedBullItemDefinition(),
            };

        private static IReadOnlyDictionary<int, CustomItemDefinition> _customItemDefinitionMap;
        public static IReadOnlyDictionary<int, CustomItemDefinition> CustomItemDefinitionMap
        {
            get
            {
                if (_customItemDefinitionMap == null)
                {
                    _customItemDefinitionMap = ItemRegistry.AllItems.ToDictionary(
                        item => (int)item.ItemType,
                        item => item
                    );
                }
                return _customItemDefinitionMap;
            }
        }

        public static CustomItemDefinition GetDefinition(ItemType type) =>
            CustomItemDefinitionMap.TryGetValue((int)type, out var d) ? d : null;

        // ItemRegistry.AllItems.FirstOrDefault(d => d.ItemType == type);

        private static readonly FieldInfo SlotsField = AccessTools.Field(
            typeof(PlayerInventory),
            "slots"
        );

        private static readonly FieldInfo AllItemField = AccessTools.Field(
            typeof(ItemCollection),
            "items"
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

        public static bool IsCustomItem(ItemType type) =>
            CustomItemDefinitionMap.ContainsKey((int)type);

        //AllItems.Any(d => d.ItemType == type);

        public static int GetMaxUses(ItemType type) => GetDefinition(type)?.MaxUses ?? 1;

        internal static ItemData GetOrCreateItemData(CustomItemDefinition def)
        {
            ItemType type = def.ItemType;
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
            AccessTools.Property(t, "Icon").SetValue(data, def.Icon);
            AccessTools.Property(t, "Prefab").SetValue(data, def.HeldModelPrefab);
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
                IssaPluginPlugin.Log.LogError(
                    $"[ItemRegistry] Could not inject custom items: could not find 'allItemData' field."
                );
                return;
            }

            var oldArray = (ItemData[])AllItemField.GetValue(collection);
            if (oldArray == null)
            {
                IssaPluginPlugin.Log.LogError(
                    $"[ItemRegistry] Could not inject custom items: could not find 'items' field."
                );
                return;
            }

            int oldSize = oldArray.Length;
            int newSize = oldSize + AllItems.Count;
            Type elementType = oldArray.GetType().GetElementType();
            ItemData[] newArray = (ItemData[])Array.CreateInstance(elementType, newSize);
            Array.Copy(oldArray, newArray, oldArray.Length);

            // Resolve both fallback sprites before the loop.
            Sprite rocketFallbackIcon = dict.TryGetValue(ItemType.RocketLauncher, out var rd)
                ? rd.Icon
                : null;
            Sprite pistolFallbackIcon = dict.TryGetValue(ItemType.DuelingPistol, out var pd)
                ? pd.Icon
                : null;

            for (int i = 0; i < AllItems.Count; i++)
            {
                CustomItemDefinition def = AllItems[i];
                var data = GetOrCreateItemData(def);
                Sprite fallbackIcon = def.UseRocketIconFallback
                    ? rocketFallbackIcon
                    : pistolFallbackIcon;
                IconProperty.SetValue(data, def.Icon ?? fallbackIcon);
                dict[def.ItemType] = data;
                newArray.SetValue(data, oldSize + i);
            }

            AllItemField.SetValue(collection, newArray);
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

                // Why this works: each XxxItem.XxxItemType is defined as (ItemType)NNN (e.g.
                // BatItemDefinition.ItemType = (ItemType)100), so (int)def.ItemType produces the same integer that was
                // previously hardcoded. This is an invariant of how custom item IDs are assigned.
                foreach (var def in AllItems)
                    addEntryMethod.Invoke(
                        table,
                        new object[] { "ITEM_" + (int)def.ItemType, def.DisplayName }
                    );

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

        /// <summary>
        /// Server handler for <see cref="GiveItemRequestMessage"/>.
        /// Registered by NetworkManagerPatches on the server.
        /// Validates the hotkey-giving configuration flag, resolves use count, and
        /// adds the requested item to the requesting player's inventory.
        /// </summary>
        internal static void ServerHandleGiveItemRequest(
            NetworkConnectionToClient conn,
            GiveItemRequestMessage msg
        )
        {
            if (!Configuration.AllowHotkeyItemGiving.Value)
            {
                IssaPluginPlugin.Log.LogInfo(
                    "[GiveItem] Rejected hotkey request: AllowHotkeyItemGiving is disabled."
                );
                return;
            }

            var inventory = conn.identity?.GetComponent<PlayerInventory>();
            if (inventory == null)
                return;

            var def = GetDefinition(msg.ItemType);
            int uses = msg.Uses > 0 ? msg.Uses : (def?.MaxUses ?? 1);
            bool added = DirectAddCustomItem(inventory, msg.ItemType, uses);
            if (!added)
                IssaPluginPlugin.Log.LogWarning("[GiveItem] Failed to add item (inventory full?).");
        }
    }
}
