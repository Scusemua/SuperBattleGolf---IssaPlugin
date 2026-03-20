using System.Reflection;
using HarmonyLib;
using IssaPlugin.Items;
using Mirror;
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
            || t.GetComponent<BomberMarker>() != null
            || t.GetComponent<DonutMarker>() != null
            || t.GetComponent<BearMarker>() != null;
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
        private static bool _wasTargetingDonut;
        private static bool _wasTargetingBear;

        internal static void ResetTargetingState()
        {
            _wasTargetingGunship = false;
            _wasTargetingBomber = false;
            _wasTargetingDonut = false;
            _wasTargetingBear = false;
        }

        static MethodBase TargetMethod() =>
            AccessTools.Method(typeof(PlayerGolfer), "TryGetBestLockOnTarget");

        static void Postfix(
            PlayerGolfer __instance,
            ref bool __result,
            ref LockOnTarget bestLockOnTarget
        )
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

            bool nowTargetingDonut =
                __result
                && bestLockOnTarget != null
                && bestLockOnTarget.GetComponent<DonutMarker>() != null;

            bool nowTargetingBear =
                __result
                && bestLockOnTarget != null
                && bestLockOnTarget.GetComponent<BearMarker>() != null;

            // ---- Bomber fallback detection ----
            // The proxy's BomberProxyBehaviour is server-only, so the client-side
            // LockOnTarget may not register with LockOnTargetManager (the base game
            // likely skips registration when Entity.AsHittable is null). Mirror the
            // approach used by OrbitalLaserLockOnIndicatorPatch: find BomberMarker
            // directly and inject it into the result so the lock-on indicator appears
            // and CmdPrepareBomberRocket is called.
            if (!nowTargetingBomber && !__result)
            {
                var bomberMarker = Object.FindFirstObjectByType<BomberMarker>();
                if (bomberMarker != null)
                {
                    var lot = bomberMarker.GetComponent<LockOnTarget>();
                    if (lot != null)
                    {
                        // Only lock on when the player is aiming toward the bomber.
                        var cam = Camera.main;
                        if (cam != null)
                        {
                            Vector3 toCraft = (
                                bomberMarker.transform.position - cam.transform.position
                            ).normalized;
                            if (Vector3.Dot(toCraft, cam.transform.forward) > 0.7f)
                            {
                                __result = true;
                                bestLockOnTarget = lot;
                                nowTargetingBomber = true;
                            }
                        }
                    }
                }
            }

            // ---- AC130 fallback detection ----
            // The gunship's Entity has no AsHittable on clients (AC130HitReceiver is
            // server-only), so its LockOnTarget may not register with LockOnTargetManager.
            // Mirror the bomber fallback: find the marker directly and inject it.
            if (!nowTargetingGunship)
            {
                var gunshipMarker = Object.FindFirstObjectByType<AC130GunshipMarker>();
                if (gunshipMarker != null)
                {
                    var lot = gunshipMarker.GetComponent<LockOnTarget>();
                    if (lot != null)
                    {
                        var cam = Camera.main;
                        if (cam != null)
                        {
                            Vector3 toCraft = (
                                gunshipMarker.transform.position - cam.transform.position
                            ).normalized;
                            if (Vector3.Dot(toCraft, cam.transform.forward) > 0.7f)
                            {
                                __result = true;
                                bestLockOnTarget = lot;
                                nowTargetingGunship = true;
                            }
                        }
                    }
                }
            }

            // ---- Gunship ----
            if (nowTargetingGunship && !_wasTargetingGunship)
                IssaPluginPlugin.Log.LogInfo("[LockOn] Locked onto AC130 gunship.");

            // Send every frame while locked on, not just the rising edge.
            // On a listen server, NetworkClient.Send() is buffered and processed
            // at end-of-frame. If the player fires in the same frame they first
            // lock on, ServerInitialize runs before the message is processed and
            // PendingGunshipHoming would be false. Sending each frame guarantees
            // the flag is set from the previous frame before any rocket fires.
            if (nowTargetingGunship)
                NetworkClient.Send(new AC130PrepareHomingMessage());

            // ---- Bomber ----
            if (nowTargetingBomber && !_wasTargetingBomber)
                IssaPluginPlugin.Log.LogInfo("[LockOn] Locked onto stealth bomber.");

            if (nowTargetingBomber)
                NetworkClient.Send(new BomberPrepareHomingMessage());

            // ---- Donut ----
            if (nowTargetingDonut && !_wasTargetingDonut)
                IssaPluginPlugin.Log.LogInfo("[LockOn] Locked onto Donut.");

            if (nowTargetingDonut)
                NetworkClient.Send(new DonutPrepareHomingMessage());

            // ---- Donut ----
            if (nowTargetingBear && !_wasTargetingBear)
                IssaPluginPlugin.Log.LogInfo("[LockOn] Locked onto Bear.");

            if (nowTargetingBear)
                NetworkClient.Send(new BearPrepareHomingMessage());

            _wasTargetingGunship = nowTargetingGunship;
            _wasTargetingBomber = nowTargetingBomber;
            _wasTargetingDonut = nowTargetingDonut;
        }
    }

    // ====================================================================
    //  Patch 4: Rocket.ServerInitialize (postfix)
    //
    //  When a rocket is spawned on the server, check if the launcher's
    //  bridge has a pending homing flag. If so, attach RocketHomingBehaviour
    //  toward the appropriate target and clear the flag.
    //
    //  RocketHomingBehaviour is generic (just needs a Transform Target) and
    //  is reused as-is for both the gunship and the bomber proxy.
    // ====================================================================
    [HarmonyPatch(typeof(Rocket), "ServerInitialize")]
    static class RocketHomingPatch
    {
        static void Postfix(Rocket __instance, PlayerInfo launcher)
        {
            if (!NetworkServer.active || launcher == null)
                return;
            if (__instance.GetComponent<CustomSpawnedRocket>() != null)
                return; // AC130/Bomber bomb. Don't home-in on those.

            // ---- AC130 gunship homing ----
            var ac130Bridge = launcher.GetComponent<AC130NetworkBridge>();
            if (ac130Bridge != null && ac130Bridge.PendingGunshipHoming)
            {
                var gunshipMarker = Object.FindFirstObjectByType<AC130GunshipMarker>();
                if (gunshipMarker != null)
                {
                    ac130Bridge.PendingGunshipHoming = false;
                    var homing = __instance.gameObject.AddComponent<RocketHomingBehaviour>();
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
                    var homing = __instance.gameObject.AddComponent<RocketHomingBehaviour>();
                    homing.Target = bomberMarker.transform;
                    IssaPluginPlugin.Log.LogInfo(
                        $"[LockOn] Rocket homing toward bomber at {bomberMarker.transform.position}."
                    );
                }
            }

            // ---- Donut homing ----
            var donutBridge = launcher.GetComponent<DonutNetworkBridge>();
            if (donutBridge != null && donutBridge.PendingDonutHoming)
            {
                var donutMarker = Object.FindFirstObjectByType<DonutMarker>();
                if (donutMarker != null)
                {
                    donutBridge.PendingDonutHoming = false;
                    var homing = __instance.gameObject.AddComponent<RocketHomingBehaviour>();
                    homing.Target = donutMarker.transform;
                    IssaPluginPlugin.Log.LogInfo(
                        $"[LockOn] Rocket homing toward Donut at {donutMarker.transform.position}."
                    );
                }
            }

            // ---- Bear homing ----
            var bearBridge = launcher.GetComponent<BearNetworkBridge>();
            if (bearBridge != null && bearBridge.PendingBearHoming)
            {
                var bearMarker = Object.FindFirstObjectByType<BearMarker>();
                if (bearMarker != null)
                {
                    bearBridge.PendingBearHoming = false;
                    var homing = __instance.gameObject.AddComponent<RocketHomingBehaviour>();
                    homing.Target = bearMarker.transform;
                    IssaPluginPlugin.Log.LogInfo(
                        $"[LockOn] Rocket homing toward Bear at {bearMarker.transform.position}."
                    );
                }
            }
        }
    }
}
