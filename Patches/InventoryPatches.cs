using System.Reflection;
using HarmonyLib;
using IssaPlugin.Items;
using Mirror;
using UnityEngine;

namespace IssaPlugin.Patches
{
    public static class InventoryPatches
    {
        private static readonly MethodInfo GetEffectiveSlotMethod =
            AccessTools.Method(typeof(PlayerInventory), "GetEffectiveSlot");

        private static InventorySlot GetEffectiveSlot(PlayerInventory instance, int index)
        {
            return (InventorySlot)GetEffectiveSlotMethod.Invoke(instance, new object[] { index });
        }

        private static bool IsCustomItem(ItemType type)
        {
            return type == BatItem.BatItemType || type == StealthBomberItem.BomberItemType;
        }

        private static int GetMaxUses(ItemType type)
        {
            if (type == BatItem.BatItemType) return Configuration.BaseballBatUses.Value;
            if (type == StealthBomberItem.BomberItemType) return Configuration.BomberUses.Value;
            return 1;
        }

        // --- Equipment display ---

        [HarmonyPatch(typeof(PlayerInventory), "UpdateEquipmentSwitchers")]
        [HarmonyPostfix]
        static void UpdateEquipmentSwitchersPostfix(PlayerInventory __instance)
        {
            var equipped = __instance.GetEffectivelyEquippedItem(false);
            if (equipped == BatItem.BatItemType)
            {
                __instance.PlayerInfo.RightHandEquipmentSwitcher
                    .SetEquipment(EquipmentType.GolfClub);
                __instance.PlayerInfo.LeftHandEquipmentSwitcher
                    .SetEquipment(EquipmentType.None);
            }
            else if (equipped == StealthBomberItem.BomberItemType)
            {
                __instance.PlayerInfo.RightHandEquipmentSwitcher
                    .SetEquipment(EquipmentType.RocketLauncher);
                __instance.PlayerInfo.LeftHandEquipmentSwitcher
                    .SetEquipment(EquipmentType.None);
            }
        }

        // --- Item use ---

        [HarmonyPatch(typeof(PlayerInventory), "TryUseItem")]
        [HarmonyPrefix]
        static bool TryUseItemPrefix(PlayerInventory __instance, bool isAirhornReaction,
            ref bool shouldEatInput, ref bool __result)
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

        // --- Animator: use default animation set for custom items ---

        [HarmonyPatch(typeof(PlayerAnimatorIo), "SetEquippedItem")]
        [HarmonyPrefix]
        static void SetEquippedItemPrefix(ref ItemType itemType)
        {
            if (IsCustomItem(itemType))
                itemType = ItemType.None;
        }

        // --- Server: allow adding custom items to inventory ---

        [HarmonyPatch(typeof(PlayerInventory), "ServerTryAddItem")]
        [HarmonyPrefix]
        static bool ServerTryAddItemPrefix(PlayerInventory __instance,
            ItemType itemToAdd, int remainingUses, ref bool __result)
        {
            if (!IsCustomItem(itemToAdd))
                return true;

            if (!NetworkServer.active)
            {
                __result = false;
                return false;
            }

            var slots = Traverse.Create(__instance).Field("slots")
                .GetValue<SyncList<InventorySlot>>();

            int emptyIndex = -1;
            for (int i = 0; i < slots.Count; i++)
            {
                if (slots[i].itemType == ItemType.None)
                {
                    emptyIndex = i;
                    break;
                }
            }

            if (emptyIndex < 0)
            {
                __result = false;
                return false;
            }

            slots[emptyIndex] = new InventorySlot(itemToAdd,
                remainingUses > 0 ? remainingUses : 1);
            __result = true;
            return false;
        }

        // --- Server command: bypass ItemData lookup for custom types ---

        [HarmonyPatch(typeof(PlayerInventory), "UserCode_CmdAddItem__ItemType")]
        [HarmonyPrefix]
        static bool CmdAddItemPrefix(PlayerInventory __instance, ItemType item)
        {
            if (!IsCustomItem(item))
                return true;

            __instance.ServerTryAddItem(item, GetMaxUses(item));
            return false;
        }

        // --- Prevent dropping custom items (no physical prefabs) ---

        [HarmonyPatch(typeof(PlayerInventory), "DropItem")]
        [HarmonyPrefix]
        static bool DropItemPrefix(PlayerInventory __instance)
        {
            return !IsCustomItem(__instance.GetEffectivelyEquippedItem(false));
        }

        // --- UI helpers: prevent errors for custom item slots ---

        [HarmonyPatch(typeof(PlayerInventory), "GetUsesForSlot")]
        [HarmonyPrefix]
        static bool GetUsesForSlotPrefix(PlayerInventory __instance, int index,
            ref int remainingUses, ref int maxUses)
        {
            InventorySlot slot = GetEffectiveSlot(__instance, index);
            if (!IsCustomItem(slot.itemType))
                return true;

            remainingUses = slot.remainingUses;
            maxUses = GetMaxUses(slot.itemType);
            return false;
        }

        [HarmonyPatch(typeof(PlayerInventory), "GetIconForSlot")]
        [HarmonyPrefix]
        static bool GetIconForSlotPrefix(PlayerInventory __instance, int index,
            ref Sprite __result)
        {
            InventorySlot slot = GetEffectiveSlot(__instance, index);
            if (!IsCustomItem(slot.itemType))
                return true;

            __result = null;
            return false;
        }
    }
}
