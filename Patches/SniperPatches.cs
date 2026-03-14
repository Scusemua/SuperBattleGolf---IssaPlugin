using System.Reflection;
using HarmonyLib;
using IssaPlugin.Items;
using UnityEngine.InputSystem;

namespace IssaPlugin.Patches
{
    /// <summary>
    /// Makes GetEffectivelyEquippedItem(false) return ItemType.ElephantGun for the
    /// sniper rifle.
    ///
    /// GetEffectivelyEquippedItem(false) returns None for all custom items because
    /// the game's visual hiding system doesn't know about them.  Every rotation and
    /// aim system downstream keys off this value:
    ///
    ///   • UpdateIsAimingItem / ShouldAim  — returns false for None  → IsAimingItem
    ///     never set by the base game
    ///   • InformIsAimingItemChanged        — sees None → uses golf-swing rotation
    ///     mode (body offset CCW from camera), not gun-aim mode (body faces camera)
    ///
    /// Returning ElephantGun makes both systems behave exactly as they do for the
    /// elephant gun: IsAimingItem becomes true naturally and the character faces the
    /// camera's aim direction rather than the swing direction.
    ///
    /// The recursive call to GetEffectivelyEquippedItem(true) inside the Postfix is
    /// safe: the Postfix exits immediately when ignoreEquipmentHiding is true, so
    /// there is no infinite recursion.
    /// </summary>
    [HarmonyPatch(typeof(PlayerInventory), "GetEffectivelyEquippedItem")]
    static class SniperGetEffectivelyEquippedItemPatch
    {
        static void Postfix(
            PlayerInventory __instance,
            bool ignoreEquipmentHiding,
            ref ItemType __result
        )
        {
            if (ignoreEquipmentHiding)
                return;
            if (__result != ItemType.None)
                return;
            if (__instance.GetEffectivelyEquippedItem(true) == SniperRifleItem.SniperRifleItemType)
            {
                __result = ItemType.ElephantGun;
            }
        }
    }

    /// <summary>
    /// Safety-net Postfix: corrects IsAimingItem for the sniper if something other
    /// than UpdateIsAimingItem resets it after SniperGetEffectivelyEquippedItemPatch
    /// has already made the base game handle it correctly.
    ///
    /// 
    /// In the normal path this Postfix is a no-op (currentlyAiming == shouldAim).
    /// </summary>
    [HarmonyPatch(typeof(PlayerInventory), "UpdateIsAimingItem")]
    static class SniperUpdateIsAimingItemPatch
    {
        private static readonly PropertyInfo IsAimingItemProp = typeof(PlayerInventory).GetProperty(
            "IsAimingItem",
            BindingFlags.Public | BindingFlags.Instance
        );

        static void Postfix(PlayerInventory __instance)
        {
            if (__instance.GetEffectivelyEquippedItem(true) != SniperRifleItem.SniperRifleItemType)
                return;

            bool shouldAim = Mouse.current?.rightButton.isPressed ?? false;
            bool currentlyAiming = (bool)(IsAimingItemProp?.GetValue(__instance) ?? false);
            bool isHoldingAimSwing = __instance.PlayerInfo?.Input?.IsHoldingAimSwing ?? false;

            IssaPluginPlugin.Log.LogInfo(
                $"[Sniper] UpdateIsAimingItem Postfix — shouldAim={shouldAim} currentlyAiming={currentlyAiming} IsHoldingAimSwing={isHoldingAimSwing}"
            );

            if (currentlyAiming == shouldAim)
                return;

            IssaPluginPlugin.Log.LogInfo(
                $"[Sniper] Correcting IsAimingItem: {currentlyAiming}→{shouldAim}, calling InformIsAimingItemChanged"
            );
            __instance.PlayerInfo.SetIsAimingItem(shouldAim);
            __instance.PlayerInfo.Movement.InformIsAimingItemChanged();
        }
    }
}
