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
    /// BNetworkManager.OnPlayerConfirmedCheatingDetected is a private method in
    /// GameAssembly.dll — Harmony can patch it by name.
    /// </summary>
    [HarmonyPatch(typeof(BNetworkManager), "OnPlayerConfirmedCheatingDetected")]
    static class SuppressCheatKickPatch
    {
        static bool Prefix() => false; // skip the kick entirely
    }
}
