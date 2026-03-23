using System.Reflection;
using HarmonyLib;

namespace IssaPlugin.Patches
{
    /// <summary>
    /// Patches out the AntiCheatPerPlayerRateChecker.RegisterHit rate limiter so
    /// rapid-fire custom items (e.g. AK47) don't trigger the game's cheat detection
    /// and kick players.
    ///
    /// AntiCheat.dll is not in our Assembly/ references, so we resolve the type at
    /// runtime via AccessTools.TypeByName and use HarmonyPatch's TargetMethod hook
    /// so it is still picked up by PatchAll.
    /// </summary>
    [HarmonyPatch]
    static class AntiCheatPerPlayerRateLimitPatch
    {
        static MethodBase TargetMethod()
        {
            var type = AccessTools.TypeByName("AntiCheatPerPlayerRateChecker");
            return AccessTools.Method(type, "RegisterHit");
        }

        static bool Prefix(ref bool __result)
        {
            __result = true;
            return false;
        }
    }
}
