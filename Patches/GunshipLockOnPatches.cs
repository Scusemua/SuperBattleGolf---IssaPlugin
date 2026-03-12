using System.Reflection;
using HarmonyLib;
using IssaPlugin.Items;
using UnityEngine;

namespace IssaPlugin.Patches
{
    // ====================================================================
    //  Patch 1: LockOnTarget.GetLockOnPosition
    //
    //  When GetLockOnPosition is called on the gunship's LockOnTarget,
    //  the base game would call AsEntity.GetTargetReticleWorldPosition(),
    //  which can crash if no TargetReticlePosition component exists.
    //  We intercept and return the gunship's transform.position directly.
    // ====================================================================
    [HarmonyPatch]
    static class GunshipLockOnPositionPatch
    {
        static MethodBase TargetMethod() =>
            AccessTools.Method(typeof(LockOnTarget), "GetLockOnPosition");

        static bool Prefix(LockOnTarget __instance, ref Vector3 __result)
        {
            if (__instance.GetComponent<AC130GunshipMarker>() == null)
                return true; // not our gunship — run normally

            // Return the gunship's world centre as the aim point.
            __result = __instance.transform.position;
            return false;
        }
    }

    // ====================================================================
    //  Patch 2: LockOnTarget.IsValid
    //
    //  The base IsValid() checks AsEntity.IsPlayer then applies player-
    //  specific guards. For our gunship AsEntity is not a player, so those
    //  guards are skipped and it returns true — but only if AsEntity is
    //  non-null. We guard the null case just in case.
    // ====================================================================
    [HarmonyPatch]
    static class GunshipLockOnIsValidPatch
    {
        static MethodBase TargetMethod() =>
            AccessTools.Method(typeof(LockOnTarget), "IsValid");

        static bool Prefix(LockOnTarget __instance, ref bool __result)
        {
            if (__instance.GetComponent<AC130GunshipMarker>() == null)
                return true; // not our gunship — run normally

            // The gunship is always a valid lock-on target while it exists.
            // (It's destroyed by the server when the session ends.)
            __result = true;
            return false;
        }
    }

    // ====================================================================
    //  Patch 3: PlayerGolfer.TryGetBestLockOnTarget (postfix)
    //
    //  After the base game picks the best lock-on target, check whether it
    //  is our gunship. If so, tell the AC130NetworkBridge so the server can
    //  flag the next rocket for gunship homing.
    //
    //  We only send the command when we're the owning player and the target
    //  changed to/from the gunship, to avoid spamming commands every frame.
    // ====================================================================
    [HarmonyPatch]
    static class GunshipLockOnDetectionPatch
    {
        private static bool _wasTargetingGunship;

        static MethodBase TargetMethod() =>
            AccessTools.Method(typeof(PlayerGolfer), "TryGetBestLockOnTarget");

        static void Postfix(
            PlayerGolfer __instance,
            bool __result,
            LockOnTarget bestLockOnTarget
        )
        {
            // Only run for the locally-owned player.
            if (!__instance.isOwned) return;

            bool nowTargetingGunship =
                __result
                && bestLockOnTarget != null
                && bestLockOnTarget.GetComponent<AC130GunshipMarker>() != null;

            // Rising edge: started targeting gunship — arm the server flag.
            if (nowTargetingGunship && !_wasTargetingGunship)
            {
                var bridge = __instance.GetComponent<AC130NetworkBridge>();
                bridge?.CmdPrepareGunshipRocket();
                IssaPluginPlugin.Log.LogInfo("[GunshipLockOn] Locked onto AC130 gunship.");
            }

            // Refresh every frame while targeting so the flag survives multiple
            // fixed-update ticks between the lock-on and the actual fire.
            if (nowTargetingGunship)
            {
                var bridge = __instance.GetComponent<AC130NetworkBridge>();
                bridge?.CmdPrepareGunshipRocket();
            }

            _wasTargetingGunship = nowTargetingGunship;
        }
    }

    // ====================================================================
    //  Patch 4: Rocket.ServerInitialize (postfix)
    //
    //  When a rocket is spawned on the server, check if the launcher's
    //  AC130NetworkBridge has the pending gunship homing flag set. If so,
    //  attach GunshipHomingBehaviour and clear the flag.
    // ====================================================================
    [HarmonyPatch(typeof(Rocket), "ServerInitialize")]
    static class GunshipRocketHomingPatch
    {
        static void Postfix(Rocket __instance, PlayerInfo launcher)
        {
            if (!Mirror.NetworkServer.active) return;
            if (launcher == null) return;

            var bridge = launcher.NetworkIdentity.GetComponent<AC130NetworkBridge>();
            if (bridge == null || !bridge.PendingGunshipHoming) return;

            var gunship = AC130NetworkBridge.ActiveGunship;
            if (gunship == null) return;

            bridge.PendingGunshipHoming = false;

            var homing = __instance.gameObject.AddComponent<GunshipHomingBehaviour>();
            homing.Target = gunship.transform;

            IssaPluginPlugin.Log.LogInfo(
                $"[GunshipLockOn] Rocket homing toward gunship at {gunship.transform.position}."
            );
        }
    }
}
