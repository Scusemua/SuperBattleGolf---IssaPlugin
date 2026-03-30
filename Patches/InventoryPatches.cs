using System.Reflection;
using HarmonyLib;
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
            int newInventorySize = (int)Configuration.NumInventorySlots.Value;
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
    /// Guards against an IndexOutOfRangeException in Hotkeys.UpdatePlayerInventoryHoykeyInternal
    /// when NumInventorySlots exceeds the base-game default (3).
    ///
    /// Root cause: Hotkeys stores a fixed-size <c>hotkeyPreviewUis</c> array whose length
    /// matches the base-game inventory size (3).  When the player enters a golf cart, the
    /// hotkey mode switches to HotkeyMode.GolfCart, and the game calls
    /// UpdatePlayerInventoryHoykeyInternal for every inventory slot.  In GolfCart mode the
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
        private static readonly FieldInfo _currentModeField =
            AccessTools.Field(typeof(Hotkeys), "currentMode");

        private static readonly FieldInfo _hotkeyPreviewUisField =
            AccessTools.Field(typeof(Hotkeys), "hotkeyPreviewUis");

        static MethodBase TargetMethod() =>
            AccessTools.Method(typeof(Hotkeys), "UpdatePlayerInventoryHoykeyInternal");

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
