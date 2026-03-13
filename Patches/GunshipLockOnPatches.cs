using System.Reflection;
using HarmonyLib;
using IssaPlugin.Items;
using UnityEngine;

namespace IssaPlugin.Patches
{
    // ====================================================================
    //  Shared helper
    // ====================================================================

    /// <summary>
    /// Returns true if the LockOnTarget belongs to one of our custom
    /// lock-on targets (AC130 gunship or stealth bomber proxy).
    /// </summary>
    internal static class CustomLockOnHelper
    {
        public static bool IsCustomTarget(LockOnTarget t) =>
            t.GetComponent<AC130GunshipMarker>() != null
            || t.GetComponent<BomberMarker>() != null;
    }

    // ====================================================================
    //  Patch 1: LockOnTarget.GetLockOnPosition
    //
    //  When GetLockOnPosition is called on one of our custom targets,
    //  the base game would call AsEntity.GetTargetReticleWorldPosition(),
    //  which can crash if no TargetReticlePosition component exists.
    //  We intercept and return the target's transform.position directly.
    // ====================================================================
    [HarmonyPatch]
    static class GunshipLockOnPositionPatch
    {
        static MethodBase TargetMethod() =>
            AccessTools.Method(typeof(LockOnTarget), "GetLockOnPosition");

        static bool Prefix(LockOnTarget __instance, ref Vector3 __result)
        {
            if (!CustomLockOnHelper.IsCustomTarget(__instance))
                return true; // not our target — run normally

            __result = __instance.transform.position;
            return false;
        }
    }

    // ====================================================================
    //  Patch 2: LockOnTarget.IsValid
    //
    //  The base IsValid() checks AsEntity.IsPlayer then applies player-
    //  specific guards. For our custom targets AsEntity is not a player,
    //  so those guards are skipped — but only if AsEntity is non-null.
    //  We guard the null case just in case.
    // ====================================================================
    [HarmonyPatch]
    static class GunshipLockOnIsValidPatch
    {
        static MethodBase TargetMethod() => AccessTools.Method(typeof(LockOnTarget), "IsValid");

        static bool Prefix(LockOnTarget __instance, ref bool __result)
        {
            if (!CustomLockOnHelper.IsCustomTarget(__instance))
                return true; // not our target — run normally

            // Custom targets are always valid while the GameObject exists.
            __result = true;
            return false;
        }
    }

    // ====================================================================
    //  Patch 3: PlayerGolfer.TryGetBestLockOnTarget (postfix)
    //
    //  After the base game picks the best lock-on target, check whether it
    //  is the gunship or the bomber proxy. If so, tell the appropriate
    //  NetworkBridge so the server can flag the next rocket for homing.
    //
    //  We refresh the flag every frame while targeting (not just on the
    //  rising edge) so it survives multiple ticks between lock-on and fire.
    // ====================================================================
    [HarmonyPatch]
    static class GunshipLockOnDetectionPatch
    {
        private static bool _wasTargetingGunship;
        private static bool _wasTargetingBomber;

        static MethodBase TargetMethod() =>
            AccessTools.Method(typeof(PlayerGolfer), "TryGetBestLockOnTarget");

        static void Postfix(PlayerGolfer __instance, bool __result, LockOnTarget bestLockOnTarget)
        {
            // Only run for the locally-owned player.
            if (!__instance.isOwned)
                return;

            bool nowTargetingGunship =
                __result
                && bestLockOnTarget != null
                && bestLockOnTarget.GetComponent<AC130GunshipMarker>() != null;

            bool nowTargetingBomber =
                __result
                && bestLockOnTarget != null
                && bestLockOnTarget.GetComponent<BomberMarker>() != null;

            // ---- Gunship ----
            if (nowTargetingGunship && !_wasTargetingGunship)
                IssaPluginPlugin.Log.LogInfo("[LockOn] Locked onto AC130 gunship.");

            if (nowTargetingGunship)
                __instance.GetComponent<AC130NetworkBridge>()?.CmdPrepareGunshipRocket();

            // ---- Bomber ----
            if (nowTargetingBomber && !_wasTargetingBomber)
                IssaPluginPlugin.Log.LogInfo("[LockOn] Locked onto stealth bomber.");

            if (nowTargetingBomber)
                __instance.GetComponent<BomberNetworkBridge>()?.CmdPrepareBomberRocket();

            _wasTargetingGunship = nowTargetingGunship;
            _wasTargetingBomber = nowTargetingBomber;
        }
    }

    // ====================================================================
    //  Patch 4: Rocket.ServerInitialize (postfix)
    //
    //  When a rocket is spawned on the server, check if the launcher's
    //  bridge has a pending homing flag. If so, attach GunshipHomingBehaviour
    //  toward the appropriate target and clear the flag.
    //
    //  GunshipHomingBehaviour is generic (just needs a Transform Target) and
    //  is reused as-is for both the gunship and the bomber proxy.
    // ====================================================================
    [HarmonyPatch(typeof(Rocket), "ServerInitialize")]
    static class GunshipRocketHomingPatch
    {
        static void Postfix(Rocket __instance, PlayerInfo launcher)
        {
            if (!Mirror.NetworkServer.active)
                return;
            if (launcher == null)
                return;

            // ---- AC130 gunship homing ----
            var ac130Bridge = launcher.GetComponent<AC130NetworkBridge>();
            if (ac130Bridge != null && ac130Bridge.PendingGunshipHoming)
            {
                var gunshipMarker = Object.FindFirstObjectByType<AC130GunshipMarker>();
                if (gunshipMarker != null)
                {
                    ac130Bridge.PendingGunshipHoming = false;
                    var homing = __instance.gameObject.AddComponent<GunshipHomingBehaviour>();
                    homing.Target = gunshipMarker.transform;
                    IssaPluginPlugin.Log.LogInfo(
                        $"[LockOn] Rocket homing toward gunship at {gunshipMarker.transform.position}."
                    );
                }
            }

            // ---- Stealth bomber homing ----
            var bomberBridge = launcher.GetComponent<BomberNetworkBridge>();
            if (bomberBridge != null && bomberBridge.PendingBomberHoming)
            {
                var bomberMarker = Object.FindFirstObjectByType<BomberMarker>();
                if (bomberMarker != null)
                {
                    bomberBridge.PendingBomberHoming = false;
                    var homing = __instance.gameObject.AddComponent<GunshipHomingBehaviour>();
                    homing.Target = bomberMarker.transform;
                    IssaPluginPlugin.Log.LogInfo(
                        $"[LockOn] Rocket homing toward bomber at {bomberMarker.transform.position}."
                    );
                }
            }
        }
    }
}
