using HarmonyLib;
using IssaPlugin.Overlays;

namespace IssaPlugin.Patches
{
    /// <summary>
    /// Number keys both answer the AC130 prompt and select a hotbar slot.
    /// While the prompt is up, slot selection is rejected so 1 and 2 only pick a mode.
    /// </summary>
    [HarmonyPatch(typeof(PlayerInventory), nameof(PlayerInventory.CanSelectItemAt))]
    static class AC130DeployPromptSlotBlockPatch
    {
        static void Postfix(int __0, ref bool __result)
        {
            if (!__result || __0 < 0)
                return;

            if (AC130DeployPrompt.SuppressesSlotSwitch)
                __result = false;
        }
    }
}
