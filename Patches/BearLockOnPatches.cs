using HarmonyLib;
using IssaPlugin.Items;
using Mirror;
using UnityEngine;

namespace IssaPlugin.Patches
{
    // ────────────────────────────────────────────────────────────────────────────
    //  Patch 1: LockOnTarget.GetLockOnPosition — return bear's world position
    // ────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Bears use BearMarker as a tag. We extend CustomLockOnHelper.IsCustomTarget
    /// in GunshipLockOnPatches.cs to also recognise BearMarker.
    ///
    /// Rather than modifying GunshipLockOnPatches.cs directly, we add a separate
    /// postfix on GetLockOnPosition that handles bears independently.
    /// </summary>
    [HarmonyPatch(typeof(LockOnTarget), "GetLockOnPosition")]
    static class BearLockOnPositionPatch
    {
        static bool Prefix(LockOnTarget __instance, ref Vector3 __result)
        {
            if (__instance.GetComponent<BearMarker>() == null)
                return true; // not a bear — run normally

            __result = __instance.transform.position + Vector3.up * 1f; // aim at bear's chest
            return false;
        }
    }

    // ────────────────────────────────────────────────────────────────────────────
    //  Patch 2: LockOnTarget.IsValid — bears are always valid lock-on targets
    // ────────────────────────────────────────────────────────────────────────────

    [HarmonyPatch(typeof(LockOnTarget), "IsValid")]
    static class BearLockOnIsValidPatch
    {
        static bool Prefix(LockOnTarget __instance, ref bool __result)
        {
            if (__instance.GetComponent<BearMarker>() == null)
                return true;

            __result = true;
            return false;
        }
    }

    // ────────────────────────────────────────────────────────────────────────────
    //  Patch 3: Rocket.ServerInitialize — attach homing to rockets fired at bears
    // ────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// When a player is locked onto a bear and fires a rocket, we attach
    /// GunshipHomingBehaviour (which already exists in the codebase) to home
    /// the rocket toward the bear. This reuses the exact same homing component
    /// used for gunship and bomber homing — no new code needed.
    ///
    /// The detection flow mirrors GunshipLockOnDetectionPatch: the client sends
    /// a BearPrepareHomingMessage each frame while locked on, the server sets
    /// PendingBearHoming on BearNetworkBridge, and this postfix on ServerInitialize
    /// consumes the flag when the next rocket spawns.
    /// </summary>
    [HarmonyPatch(typeof(Rocket), "ServerInitialize")]
    static class BearRocketHomingPatch
    {
        static void Postfix(Rocket __instance, PlayerInfo launcher)
        {
            if (!NetworkServer.active || launcher == null)
                return;

            var bearBridge = launcher.GetComponent<BearNetworkBridge>();
            if (bearBridge == null || !bearBridge.PendingBearHoming)
                return;

            // Find the nearest bear to home toward
            var nearestBear = FindNearestBear(__instance.transform.position);
            if (nearestBear == null)
                return;

            bearBridge.PendingBearHoming = false;

            var homing = __instance.gameObject.AddComponent<GunshipHomingBehaviour>();
            homing.Target = nearestBear.transform;

            IssaPluginPlugin.Log.LogInfo(
                $"[Bear] Rocket homing toward bear at {nearestBear.transform.position}."
            );
        }

        private static BearMarker FindNearestBear(Vector3 from)
        {
            var bears = Object.FindObjectsByType<BearMarker>(FindObjectsSortMode.None);
            BearMarker best = null;
            float bestDist = float.MaxValue;

            foreach (var bear in bears)
            {
                float d = Vector3.Distance(from, bear.transform.position);
                if (d < bestDist)
                {
                    bestDist = d;
                    best = bear;
                }
            }

            return best;
        }
    }

    // ────────────────────────────────────────────────────────────────────────────
    //  Patch 4: PlayerGolfer.TryGetBestLockOnTarget — detect bear lock-on
    // ────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Mirrors GunshipLockOnDetectionPatch.Postfix but for bears.
    /// Sends BearPrepareHomingMessage to the server each frame while a bear
    /// is locked on so the server can flag the next rocket for homing.
    ///
    /// Also injects the bear LockOnTarget into the result if the bear's
    /// LockOnTarget did not self-register with LockOnTargetManager
    /// (same fallback used for the AC130 and bomber).
    /// </summary>
    [HarmonyPatch(typeof(PlayerGolfer), "TryGetBestLockOnTarget")]
    static class BearLockOnDetectionPatch
    {
        private static bool _wasTargetingBear;

        // Called from Plugin.OnMatchStateChanged to reset between holes
        public static void Reset() => _wasTargetingBear = false;

        static void Postfix(
            PlayerGolfer __instance,
            ref bool __result,
            ref LockOnTarget bestLockOnTarget
        )
        {
            if (!__instance.isOwned)
                return;

            bool nowTargetingBear =
                __result
                && bestLockOnTarget != null
                && bestLockOnTarget.GetComponent<BearMarker>() != null;

            // Fallback: inject the nearest bear's LockOnTarget if it wasn't
            // picked up by LockOnTargetManager
            if (!nowTargetingBear)
            {
                var nearestBear = FindNearestBearInView();
                if (nearestBear != null)
                {
                    var lot = nearestBear.GetComponent<LockOnTarget>();
                    if (lot != null)
                    {
                        __result = true;
                        bestLockOnTarget = lot;
                        nowTargetingBear = true;
                    }
                }
            }

            if (nowTargetingBear && !_wasTargetingBear)
                IssaPluginPlugin.Log.LogInfo("[LockOn] Locked onto bear.");

            // Send every frame while locked on — same reasoning as gunship/bomber:
            // mirror buffers messages end-of-frame, so rising-edge-only risks the
            // flag being false when ServerInitialize runs in the same frame as lock-on.
            if (nowTargetingBear)
                NetworkClient.Send(new BearPrepareHomingMessage());

            _wasTargetingBear = nowTargetingBear;
        }

        private static BearMarker FindNearestBearInView()
        {
            var cam = Camera.main;
            if (cam == null)
                return null;

            var bears = Object.FindObjectsByType<BearMarker>(FindObjectsSortMode.None);
            BearMarker best = null;
            float bestDot = 0.7f; // minimum forward alignment

            foreach (var bear in bears)
            {
                Vector3 toTarget = (bear.transform.position - cam.transform.position).normalized;
                float dot = Vector3.Dot(toTarget, cam.transform.forward);
                if (dot > bestDot)
                {
                    bestDot = dot;
                    best = bear;
                }
            }

            return best;
        }
    }
}
