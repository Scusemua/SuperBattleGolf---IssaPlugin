using HarmonyLib;

namespace IssaPlugin.Patches
{
    /// <summary>
    /// Guards against a NullReferenceException in PlayerMovement.IsKnockoutProtectedFromPlayer.
    ///
    /// The base game bug: CourseManager.PlayerKnockoutStreaks returns null when
    /// CourseManager has no instance (during scene teardown / match end).
    /// IsKnockoutProtectedFromPlayer calls .TryGetValue(...) on the null dictionary
    /// unconditionally, throwing NullReferenceException.
    ///
    /// This is triggered as players are being destroyed at round end:
    ///   PlayerMovement.IsKnockoutProtectedFromPlayer
    ///   PlayerMovement.UpdateKnockoutImmunityVfx
    ///   PlayerMovement.OnWillBeDestroyed
    ///   PlayerInfo.OnWillBeDestroyed
    ///   Entity.OnWillBeDestroyed
    ///   Entity.OnDestroy
    ///
    /// Fix: if PlayerKnockoutStreaks is null, skip the base method and return false —
    /// no knockout protection can exist when the CourseManager is gone.
    /// </summary>
    [HarmonyPatch(typeof(PlayerMovement), "IsKnockoutProtectedFromPlayer")]
    static class KnockoutProtectionNullRefPatch
    {
        static bool Prefix(ref bool __result)
        {
            if (CourseManager.PlayerKnockoutStreaks == null)
            {
                __result = false;
                return false;
            }
            return true;
        }
    }
}
