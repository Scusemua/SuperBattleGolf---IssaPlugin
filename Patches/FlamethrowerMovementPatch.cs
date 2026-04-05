using System.Reflection;
using HarmonyLib;
using IssaPlugin.Items;
using Mirror;
using UnityEngine;

namespace IssaPlugin.Patches
{
    /// <summary>
    /// Harmony prefix on PlayerMovement.ProcessMovementInput.
    ///
    /// While the local player is on fire (FlamethrowerNetworkBridge.LocalPlayerIsBurning),
    /// this patch overwrites the player's moveVector2d (the input vector that
    /// ProcessMovementInput reads to derive world-space movement) with a slowly
    /// rotating random direction.  The result is that the burning player runs in
    /// erratic circles regardless of what keys they hold — the "panic" effect.
    ///
    /// The panic direction changes at a random rate every 0.2–0.6 seconds, giving
    /// a frantic but readable motion rather than pure frame-to-frame noise.
    ///
    /// ProcessMovementInput is private, so we locate it at static init time via
    /// AccessTools (same approach as FreezeMovementPatch → ApplyHorizontalDrag).
    /// moveVector2d is a public field on PlayerMovement, so only one FieldInfo
    /// reflection call is needed per frame.
    ///
    /// Guard: only runs on the LOCAL player's PlayerMovement instance.  Remote
    /// players' bridges set their own LocalPlayerIsBurning when they are the victim.
    /// </summary>
    [HarmonyPatch]
    static class FlamethrowerMovementPatch
    {
        private static readonly MethodBase TargetMb;
        private static readonly FieldInfo MoveVector2dField;

        // Panic motion state — static because only one local player exists.
        private static float _panicAngle;
        private static float _panicAngleChangeRate; // radians / second
        private static float _panicDirectionTimer; // seconds until next direction change

        static FlamethrowerMovementPatch()
        {
            var pmt = AccessTools.TypeByName("PlayerMovement");
            if (pmt == null)
            {
                IssaPluginPlugin.Log.LogWarning(
                    "[Flamethrower] PlayerMovement type not found — panic movement patch skipped."
                );
                return;
            }

            TargetMb = AccessTools.Method(pmt, "ProcessMovementInput");
            if (TargetMb == null)
            {
                IssaPluginPlugin.Log.LogWarning(
                    "[Flamethrower] PlayerMovement.ProcessMovementInput not found — panic movement patch skipped."
                );
                return;
            }

            MoveVector2dField = AccessTools.Field(pmt, "moveVector2d");
            if (MoveVector2dField == null)
            {
                IssaPluginPlugin.Log.LogWarning(
                    "[Flamethrower] PlayerMovement.moveVector2d not found — panic movement patch skipped."
                );
            }

            IssaPluginPlugin.Log.LogInfo("[Flamethrower] Panic movement patch ready.");
        }

        static MethodBase TargetMethod() => TargetMb;

        static void Prefix(object __instance)
        {
            if (!FlamethrowerNetworkBridge.LocalPlayerIsBurning)
                return;

            if (MoveVector2dField == null)
                return;

            // Only modify the LOCAL player's input — remote PlayerMovement
            // instances on this machine should be unaffected.
            var nb = __instance as NetworkBehaviour;
            if (nb == null || !nb.isLocalPlayer)
                return;

            // Advance panic direction timer.
            _panicDirectionTimer -= Time.deltaTime;
            if (_panicDirectionTimer <= 0f)
            {
                // Pick a new random angular velocity and duration.
                _panicAngleChangeRate = Random.Range(-5f, 5f); // rad/s
                _panicDirectionTimer = Random.Range(0.2f, 0.6f);
            }

            _panicAngle += _panicAngleChangeRate * Time.deltaTime;

            // Inject a unit-magnitude panic input vector.
            // ProcessMovementInput will then rotate it by the camera yaw,
            // so the panic direction is relative to the camera — matching
            // normal movement semantics and giving appropriate visual results.
            var panicDir = new Vector2(Mathf.Sin(_panicAngle), Mathf.Cos(_panicAngle));
            MoveVector2dField.SetValue(__instance, panicDir);
        }
    }
}
