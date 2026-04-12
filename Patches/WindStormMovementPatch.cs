using System.Reflection;
using HarmonyLib;
using IssaPlugin.Items;
using Mirror;
using UnityEngine;

namespace IssaPlugin.Patches
{
    /// <summary>
    /// Harmony postfix on PlayerMovement.FixedUpdate.
    ///
    /// While a Wind Storm is active, applies an additional acceleration force to the
    /// local player's Rigidbody in the direction of the current wind — but only while
    /// the player is in the knocked-out state.  Upright players can brace against the wind;
    /// limp ragdoll bodies cannot.
    ///
    /// The game's own wind system only affects golf balls (Hittable.ShouldApplyWind
    /// explicitly returns false for players), so this patch fills the gap for KO'd bodies.
    ///
    /// Force model: rigidbody.AddForce(WindManager.Wind * PlayerWindFactor, Acceleration)
    ///   — mass-independent, applied every fixed frame, scales with storm wind speed.
    ///   At the default storm speed of 150 and factor 0.15, this is 22.5 units/s²,
    ///   which will visibly tumble a KO'd player across the course.
    ///
    /// Guards:
    ///   • Only runs when a storm session is active (WindStormNetworkBridge.IsSessionActive).
    ///   • Only runs for the local player (mirrors the game's own FixedUpdate guard).
    ///   • Only runs when the player is in the IsKnockedOut state (not Recovering).
    ///   • Skips the activator if they are in WindImmuneNetIds (ExcludeActivator config).
    ///   • Skips entirely when AffectPlayers config is false.
    /// </summary>
    [HarmonyPatch]
    static class WindStormMovementPatch
    {
        private static readonly MethodBase TargetMb;
        private static readonly FieldInfo RigidbodyField;
        private static readonly PropertyInfo IsKnockedOutProp;

        static WindStormMovementPatch()
        {
            var pmt = AccessTools.TypeByName("PlayerMovement");
            if (pmt == null)
            {
                IssaPluginPlugin.Log.LogWarning(
                    "[WindStorm] PlayerMovement type not found — player wind patch skipped."
                );
                return;
            }

            TargetMb = AccessTools.Method(pmt, "FixedUpdate");
            if (TargetMb == null)
            {
                IssaPluginPlugin.Log.LogWarning(
                    "[WindStorm] PlayerMovement.FixedUpdate not found — player wind patch skipped."
                );
                return;
            }

            RigidbodyField = AccessTools.Field(pmt, "rigidbody");
            if (RigidbodyField == null)
            {
                IssaPluginPlugin.Log.LogWarning(
                    "[WindStorm] PlayerMovement.rigidbody field not found — player wind patch skipped."
                );
            }

            IsKnockedOutProp = AccessTools.Property(pmt, "IsKnockedOut");
            if (IsKnockedOutProp == null)
            {
                IssaPluginPlugin.Log.LogWarning(
                    "[WindStorm] PlayerMovement.IsKnockedOut property not found — player wind patch skipped."
                );
            }

            IssaPluginPlugin.Log.LogInfo("[WindStorm] Player wind movement patch ready.");
        }

        static MethodBase TargetMethod() => TargetMb;

        static void Postfix(object __instance)
        {
            if (!WindStormNetworkBridge.IsSessionActive)
                return;
            if (!ModConfig.WindStorm.AffectPlayers.Value)
                return;
            if (WindManager.CurrentWindSpeed <= 0)
                return;
            if (RigidbodyField == null || IsKnockedOutProp == null)
                return;

            // Mirror the game's own guard — FixedUpdate bails early for non-local players.
            var nb = __instance as NetworkBehaviour;
            if (nb == null || !nb.isLocalPlayer)
                return;

            // Only apply wind to knocked-out players — upright players can resist the wind.
            if (!(bool)IsKnockedOutProp.GetValue(__instance))
                return;

            // Honour the activator immunity list (same set checked by ball-wind patches).
            var ni = ((Component)__instance).GetComponent<NetworkIdentity>();
            if (ni != null && WindStormNetworkBridge.WindImmuneNetIds.Contains(ni.netId))
                return;

            var rb = RigidbodyField.GetValue(__instance) as Rigidbody;
            if (rb == null)
                return;

            float factor = ModConfig.WindStorm.PlayerWindFactor.Value;
            rb.AddForce(WindManager.Wind * factor, ForceMode.Acceleration);
        }
    }
}
