using System.Reflection;
using HarmonyLib;
using IssaPlugin.Items;
using UnityEngine.InputSystem;

namespace IssaPlugin.Patches
{
    /// <summary>
    /// Corrects IsAimingItem for the sniper rifle after UpdateIsAimingItem runs.
    ///
    /// UpdateIsAimingItem's ShouldAim helper always returns false for custom items
    /// because GetEffectivelyEquippedItem(false) returns None. We cannot simply
    /// block the method (the original Prefix approach) because that also loses
    /// the InformIsAimingItemChanged() call that drives the movement speed and
    /// the animator's aim pose — which is exactly why aiming never worked while
    /// stationary.
    ///
    /// Instead we let the method run, then immediately correct the result using
    /// raw right-click input.  If the value changed we re-invoke
    /// InformIsAimingItemChanged() so the movement system picks up the fix.
    /// </summary>
    [HarmonyPatch(typeof(PlayerInventory), "UpdateIsAimingItem")]
    static class SniperUpdateIsAimingItemPatch
    {
        private static readonly PropertyInfo IsAimingItemProp =
            typeof(PlayerInventory).GetProperty(
                "IsAimingItem",
                BindingFlags.Public | BindingFlags.Instance
            );

        static void Postfix(PlayerInventory __instance)
        {
            if (__instance.GetEffectivelyEquippedItem(true) != SniperRifleItem.SniperRifleItemType)
                return;

            bool shouldAim = Mouse.current?.rightButton.isPressed ?? false;
            bool currentlyAiming = (bool)(IsAimingItemProp?.GetValue(__instance) ?? false);

            if (currentlyAiming == shouldAim)
                return;

            IsAimingItemProp?.SetValue(__instance, shouldAim);
            __instance.PlayerInfo.Movement.InformIsAimingItemChanged();
        }
    }
}
