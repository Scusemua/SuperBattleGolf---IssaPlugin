using HarmonyLib;
using IssaPlugin.Items;

namespace IssaPlugin.Patches
{
    /// <summary>
    /// Blocks PlayerInventory.UpdateIsAimingItem from running its built-in logic
    /// while the sniper rifle is equipped.
    ///
    /// UpdateIsAimingItem's ShouldAim helper sees None for custom items (because
    /// GetEffectivelyEquippedItem(false) returns None) and would always set
    /// IsAimingItem = false, overriding the value SniperScopeOverlay.Update()
    /// writes every frame.
    /// </summary>
    [HarmonyPatch(typeof(PlayerInventory), "UpdateIsAimingItem")]
    static class SniperUpdateIsAimingItemPatch
    {
        static bool Prefix(PlayerInventory __instance)
        {
            // Let the original run for all non-sniper items.
            // For the sniper, SniperScopeOverlay.Update() drives IsAimingItem
            // directly each frame — skip the game's override.
            return __instance.GetEffectivelyEquippedItem(true)
                != SniperRifleItem.SniperRifleItemType;
        }
    }
}
