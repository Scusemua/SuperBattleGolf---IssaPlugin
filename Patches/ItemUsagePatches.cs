using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using IssaPlugin.Items;
using Mirror;
using UnityEngine;

namespace IssaPlugin.Patches
{
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
                var bridge = __instance.GetComponent<MissileNetworkBridge>();
                if (bridge != null)
                    bridge.CmdRequestMissile();
                else
                    IssaPluginPlugin.Log.LogError("[Missile] No MissileNetworkBridge on player.");
                return false;
            }

            if (equipped == AC130Item.AC130ItemType)
            {
                shouldEatInput = true;
                __result = true;
                var bridge = __instance.GetComponent<AC130NetworkBridge>();
                if (bridge != null)
                    bridge.CmdStartAC130();
                else
                    IssaPluginPlugin.Log.LogError("[AC130] No AC130NetworkBridge on player.");
                return false;
            }

            if (equipped == FreezeItem.FreezeItemType)
            {
                shouldEatInput = true;
                __result = true;
                var bridge = __instance.GetComponent<FreezeNetworkBridge>();
                if (bridge != null)
                    bridge.CmdActivateFreeze();
                else
                    IssaPluginPlugin.Log.LogError("[Freeze] No FreezeNetworkBridge on player.");
                return false;
            }

            return true;
        }
    }

    [HarmonyPatch]
    static class UpdateEquipmentSwitchersPatch
    {
        private static readonly Dictionary<PlayerInventory, CustomEquipState> _states = new();

        static MethodBase TargetMethod() =>
            AccessTools.Method(typeof(PlayerInventory), "UpdateEquipmentSwitchers");

        static void Postfix(PlayerInventory __instance)
        {
            var equipped = __instance.GetEffectivelyEquippedItem(true);
            var rightSwitcher = __instance.PlayerInfo.RightHandEquipmentSwitcher;

            if (!ItemRegistry.IsCustomItem(equipped))
            {
                ClearCustomModel(__instance);
                ShowDefaultEquipment(rightSwitcher);
                return;
            }

            if (
                equipped == StealthBomberItem.BomberItemType
                || equipped == PredatorMissileItem.MissileItemType
                || equipped == AC130Item.AC130ItemType
                || equipped == FreezeItem.FreezeItemType
            )
            {
                rightSwitcher.SetEquipment(EquipmentType.RocketLauncher);
                __instance.PlayerInfo.LeftHandEquipmentSwitcher.SetEquipment(EquipmentType.None);
            }

            var prefab = GetPrefabForItem(equipped);
            if (prefab == null)
                return;

            if (
                !_states.TryGetValue(__instance, out var state)
                || state.ItemType != equipped
                || state.Model == null
            )
            {
                ClearCustomModel(__instance);

                var model = Object.Instantiate(prefab);
                model.transform.SetParent(rightSwitcher.transform, false);
                model.transform.localPosition = Vector3.zero;
                model.transform.localRotation = Quaternion.identity;
                model.transform.localScale = Vector3.one;
                model.SetActive(true);

                SetLayerRecursive(model, rightSwitcher.gameObject.layer);

                _states[__instance] = new CustomEquipState { Model = model, ItemType = equipped };

                IssaPluginPlugin.Log.LogInfo(
                    $"[Equipment] Custom model spawned for item {(int)equipped}."
                );

                HideDefaultEquipment(rightSwitcher);
            }
        }

        private static void HideDefaultEquipment(EquipmentSwitcher switcher)
        {
            if (switcher.CurrentEquipment == null)
                return;

            foreach (
                var r in switcher.CurrentEquipment.gameObject.GetComponentsInChildren<Renderer>()
            )
                r.enabled = false;
        }

        private static void ClearCustomModel(PlayerInventory inventory)
        {
            if (!_states.TryGetValue(inventory, out var state))
                return;

            if (state.Model != null)
                Object.Destroy(state.Model);

            _states.Remove(inventory);
        }

        private static GameObject GetPrefabForItem(ItemType type)
        {
            if (type == BatItem.BatItemType)
                return AssetLoader.BatModelPrefab;
            if (type == StealthBomberItem.BomberItemType)
                return AssetLoader.BomberTabletPrefab;
            if (type == PredatorMissileItem.MissileItemType)
                return AssetLoader.MissileTabletPrefab;
            if (type == AC130Item.AC130ItemType)
                return AssetLoader.MissileTabletPrefab;
            if (type == FreezeItem.FreezeItemType)
                return AssetLoader.FreezeModelPrefab;
            return null;
        }

        private static void ShowDefaultEquipment(EquipmentSwitcher switcher)
        {
            if (switcher.CurrentEquipment == null)
                return;

            foreach (
                var r in switcher.CurrentEquipment.gameObject.GetComponentsInChildren<Renderer>()
            )
                r.enabled = true;
        }

        private static void SetLayerRecursive(GameObject go, int layer)
        {
            go.layer = layer;
            foreach (Transform child in go.transform)
                SetLayerRecursive(child.gameObject, layer);
        }

        private struct CustomEquipState
        {
            public GameObject Model;
            public ItemType ItemType;
        }
    }

    /// <summary>
    /// Blocks the golf swing charge when a non-bat custom item is equipped.
    ///
    /// The Swing input action has two independent paths:
    ///   1. AddInputBuffer → UseItem() → TryUseItem() [our existing patch handles this]
    ///   2. Swing.started → StartChargingSwing → TryStartChargingSwing (this path)
    ///      Swing.canceled → FinishChargingSwing → ReleaseSwingCharge → fires swing
    ///
    /// Without this patch, path 2 fires a golf swing even though path 1 correctly
    /// consumed the item. Blocking TryStartChargingSwing keeps IsChargingSwing false,
    /// so ReleaseSwingChargeInternal's own CanReleaseSwingCharge guard is a no-op.
    /// The bat is excluded because it intentionally uses the swing mechanic.
    /// </summary>
    [HarmonyPatch]
    static class SwingChargePatch
    {
        static MethodBase TargetMethod() =>
            AccessTools.Method(typeof(PlayerGolfer), "TryStartChargingSwing");

        static bool Prefix(PlayerGolfer __instance, ref bool __result)
        {
            var inventory = __instance.GetComponent<PlayerInventory>();
            if (inventory == null)
                return true;

            var equipped = inventory.GetEffectivelyEquippedItem(true);
            if (ItemRegistry.IsCustomItem(equipped) && equipped != BatItem.BatItemType)
            {
                __result = false;
                return false;
            }

            return true;
        }
    }

    [HarmonyPatch]
    static class DropItemPatch
    {
        private static readonly MethodInfo RemoveItemAtMethod = AccessTools.Method(
            typeof(PlayerInventory),
            "RemoveItemAt"
        );

        static MethodBase TargetMethod() => AccessTools.Method(typeof(PlayerInventory), "DropItem");

        static bool Prefix(PlayerInventory __instance)
        {
            if (!ItemRegistry.IsCustomItem(__instance.GetEffectivelyEquippedItem(true)))
                return true;

            int index = __instance.EquippedItemIndex;
            if (index < 0)
                return false;

            RemoveItemAtMethod.Invoke(__instance, new object[] { index, false });
            return false;
        }
    }

    [HarmonyPatch(typeof(Rocket), "Start")]
    static class RocketStartPatch
    {
        static void Postfix(Rocket __instance)
        {
            if (!PredatorMissileItem.ActiveMissileRockets.Contains(__instance))
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
            if (!PredatorMissileItem.ActiveMissileRockets.Contains(__instance))
                return;

            DistanceField?.SetValue(__instance, 0f);
        }
    }
}
