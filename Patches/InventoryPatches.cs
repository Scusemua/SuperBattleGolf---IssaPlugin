using System.Reflection;
using HarmonyLib;
using IssaPlugin.Items;
using UnityEngine;

namespace IssaPlugin.Patches
{
    [HarmonyPatch(typeof(GameManager))]
    public class GameManagerPatch
    {
        private static readonly FieldInfo _settingsField = AccessTools.Field(
            typeof(GameManager),
            "playerInventorySettings"
        );

        private static readonly MethodInfo _maxItemsSetter = AccessTools.PropertySetter(
            typeof(PlayerInventorySettings),
            "MaxItems"
        );

        [HarmonyPatch("Awake")]
        [HarmonyPostfix]
        private static void AwakePatch(GameManager __instance)
        {
            int newInventorySize = (int)ModConfig.Global.NumInventorySlots.Value;
            if (newInventorySize == 3)
                return; // Same as base game.

            var settings = (PlayerInventorySettings)_settingsField.GetValue(__instance);
            if (settings == null)
            {
                IssaPluginPlugin.Log.LogInfo(
                    $"[GameManager::PlayerInventorySettings] Failed to increase player inventory size."
                );
                return;
            }
            _maxItemsSetter.Invoke(settings, new object[] { newInventorySize });
        }
    }

    /// <summary>
    /// Ensures the player re-equips the golf club when a custom item's last use is consumed.
    ///
    /// Root cause: custom item routines call SetCurrentItemUse(Regular) before
    /// DecrementAndRemove, so IsUsingItemAtAll is true when RemoveItemAt fires.
    /// RemoveItemAt calls TryDeselectItem, but TryDeselectItem's CanDeselectItem guard
    /// returns false (IsUsingItemAtAll == true), leaving EquippedItemIndex pointing at
    /// the now-empty slot and keeping the item label visible.
    ///
    /// Fix: postfix on RemoveItemAt — if EquippedItemIndex still points at the removed
    /// slot after the call, TryDeselectItem failed. Reset CurrentItemUse to None and
    /// retry so the golf club is correctly re-equipped.
    /// </summary>
    [HarmonyPatch]
    static class RemoveItemAtForcedDeselectPatch
    {
        static MethodBase TargetMethod() =>
            AccessTools.Method(typeof(PlayerInventory), "RemoveItemAt");

        static void Postfix(PlayerInventory __instance, int index)
        {
            if (!__instance.isLocalPlayer)
                return;
            if (__instance.EquippedItemIndex != index)
                return;

            // TryDeselectItem() failed — EquippedItemIndex still points at the removed slot.
            // Clear the use state so CanDeselectItem passes, then retry.
            ItemHelper.SetCurrentItemUse(__instance, ItemUseType.None);
            __instance.TryDeselectItem(false, false);
        }
    }

    /// <summary>
    /// Guards against an IndexOutOfRangeException in Hotkeys.UpdatePlayerInventoryHotkeyInternal
    /// when NumInventorySlots exceeds the base-game default (3).
    ///
    /// Root cause: Hotkeys stores a fixed-size <c>hotkeyPreviewUis</c> array whose length
    /// matches the base-game inventory size (3).  When the player enters a golf cart, the
    /// hotkey mode switches to HotkeyMode.GolfCart, and the game calls
    /// UpdatePlayerInventoryHotkeyInternal for every inventory slot.  In GolfCart mode the
    /// method indexes directly into <c>hotkeyPreviewUis[inventorySlotIndex]</c> without a
    /// bounds check, so any slot index ≥ hotkeyPreviewUis.Length throws
    /// IndexOutOfRangeException and crashes the game.
    ///
    /// Fix: skip the update for slot indices that have no corresponding preview UI element.
    /// The extra slots simply don't appear in the golf-cart HUD, which is the correct
    /// behaviour since the preview strip has no physical space for them anyway.
    /// </summary>
    [HarmonyPatch]
    static class HotkeysGolfCartPreviewBoundsPatch
    {
        private static readonly FieldInfo _currentModeField = AccessTools.Field(
            typeof(Hotkeys),
            "currentMode"
        );

        private static readonly FieldInfo _hotkeyPreviewUisField = AccessTools.Field(
            typeof(Hotkeys),
            "hotkeyPreviewUis"
        );

        static MethodBase TargetMethod() =>
            AccessTools.Method(typeof(Hotkeys), "UpdatePlayerInventoryHotkeyInternal");

        static bool Prefix(Hotkeys __instance, int inventorySlotIndex)
        {
            // Only the GolfCart path directly indexes hotkeyPreviewUis by slot index.
            // Other modes use hotkeyUis[hotkeyIndex] which is sized differently and safe.
            var mode = (HotkeyMode)_currentModeField.GetValue(__instance);
            if (mode != HotkeyMode.GolfCart)
                return true; // let the base method run normally

            var previewUis = (UnityEngine.Object[])_hotkeyPreviewUisField.GetValue(__instance);
            if (previewUis != null && inventorySlotIndex < previewUis.Length)
                return true; // within bounds — run normally

            // Index is out of range for the preview strip; skip silently.
            return false;
        }
    }
}
