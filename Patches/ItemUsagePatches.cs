using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using IssaPlugin.Items;
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
                IssaPluginPlugin.Log.LogInfo($"[Equipment] Using Baseball Bat item.");
                shouldEatInput = false;
                __result = false;
                return false;
            }

            if (equipped == StealthBomberItem.BomberItemType)
            {
                IssaPluginPlugin.Log.LogInfo($"[Equipment] Using Stealth Bomber item.");
                shouldEatInput = true;
                __result = true;
                __instance.StartCoroutine(StealthBomberItem.BomberRunRoutine(__instance));
                return false;
            }

            if (equipped == PredatorMissileItem.MissileItemType)
            {
                IssaPluginPlugin.Log.LogInfo($"[Equipment] Using Predator Missile item.");
                shouldEatInput = true;
                __result = true;
                __instance.StartCoroutine(PredatorMissileItem.MissileRoutine(__instance));
                return false;
            }

            if (equipped == AC130Item.AC130ItemType)
            {
                IssaPluginPlugin.Log.LogInfo($"[Equipment] Using AC130 item.");
                shouldEatInput = true;
                __result = true;
                __instance.StartCoroutine(AC130Item.AC130Routine(__instance));
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

                IssaPluginPlugin.Log.LogInfo($"[Equipment] Hiding default equipment.");
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
}
