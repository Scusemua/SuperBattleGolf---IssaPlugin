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
                __instance.PlayerInfo.LeftHandEquipmentSwitcher.SetEquipment(EquipmentType.None);
            }
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
