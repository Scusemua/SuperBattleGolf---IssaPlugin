using System.Reflection;
using HarmonyLib;

namespace IssaPlugin.Patches
{
    /// <summary>
    /// Prevents the game's anti-cheat system from kicking players.
    ///
    /// Rapid-fire custom items (e.g. AK47) exceed several server-side rate limiters
    /// (elephant gun VFX at 0.25 s min, HitWithItem at 0.5 s min). Rather than
    /// trying to patch every individual AntiCheatPerPlayerRateChecker instance,
    /// we suppress the kick at the single point where confirmed cheating results
    /// in a player being disconnected.
    ///
    /// In game v4 the old OnPlayerConfirmedCheatingDetected was split into
    /// per-source handlers that all funnel into OnConfirmedCheating(int, string).
    /// </summary>
    [HarmonyPatch(typeof(BNetworkManager), "OnConfirmedCheating")]
    static class SuppressCheatKickPatch
    {
        static bool Prefix() => false;
    }
}
