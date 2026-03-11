using System.Reflection;
using HarmonyLib;
using IssaPlugin.Items;
using UnityEngine;
using UnityEngine.InputSystem;

namespace IssaPlugin.Patches
{
    /// <summary>
    /// Harmony postfix on PlayerMovement.ApplyHorizontalDrag.
    ///
    /// Normally, ApplyHorizontalDrag multiplies the player's velocity by:
    ///   1 - (horizontalDragFactor × DefaultHorizontalDrag × fixedDeltaTime)
    /// This both caps top speed (when input is held) and provides deceleration
    /// (when input is released). Lowering DefaultHorizontalDrag globally breaks
    /// both — the player accelerates very slowly AND slides. We need to separate
    /// the two cases.
    ///
    /// Strategy: leave DefaultHorizontalDrag untouched (speed caps normally when
    /// pressing movement keys). In the Postfix, when frozen and no movement key is
    /// held, discard the drag the game just applied and substitute a tiny ice drag
    /// instead — so the player slides rather than stopping abruptly.
    /// </summary>
    [HarmonyPatch]
    static class FreezeMovementPatch
    {
        // Reflected access to PlayerMovement.Velocity.
        // Cached once; null means the type or member was not found.
        private static readonly MethodBase TargetMb;
        private static readonly PropertyInfo VelocityProp;
        private static readonly FieldInfo    VelocityField;

        static FreezeMovementPatch()
        {
            var pmt = AccessTools.TypeByName("PlayerMovement");
            if (pmt == null)
            {
                IssaPluginPlugin.Log.LogWarning(
                    "[Freeze] PlayerMovement type not found — walking slide patch skipped."
                );
                return;
            }

            TargetMb = AccessTools.Method(pmt, "ApplyHorizontalDrag");
            if (TargetMb == null)
            {
                IssaPluginPlugin.Log.LogWarning(
                    "[Freeze] PlayerMovement.ApplyHorizontalDrag not found — slide patch skipped."
                );
                return;
            }

            // Velocity may be a public property or a private field depending on the build.
            VelocityProp  = AccessTools.Property(pmt, "Velocity");
            VelocityField = VelocityProp == null
                ? AccessTools.Field(pmt, "velocity")
                : null;

            IssaPluginPlugin.Log.LogInfo(
                $"[Freeze] Movement patch ready. Velocity via "
                + $"{(VelocityProp != null ? "property" : "field")}."
            );
        }

        static MethodBase TargetMethod() => TargetMb;

        // Capture velocity BEFORE the game's drag runs.
        static void Prefix(object __instance, out Vector3 __state)
        {
            __state = GetVelocity(__instance);
        }

        // If frozen and the player is not actively pressing movement keys,
        // replace the post-drag velocity with an ice-drag version.
        static void Postfix(object __instance, Vector3 __state)
        {
            if (!FreezeItem.IsFrozen) return;

            // Only apply ice slide when no movement input is held.
            var kb = Keyboard.current;
            bool hasInput = kb != null
                && (kb[Key.W].isPressed || kb[Key.A].isPressed
                    || kb[Key.S].isPressed || kb[Key.D].isPressed
                    || kb[Key.UpArrow].isPressed   || kb[Key.DownArrow].isPressed
                    || kb[Key.LeftArrow].isPressed || kb[Key.RightArrow].isPressed);

            if (hasInput) return; // Moving normally — let normal drag cap speed.

            Vector3 prevVel = __state;
            float horizSqr = prevVel.x * prevVel.x + prevVel.z * prevVel.z;
            if (horizSqr < 0.01f) return; // Already essentially stopped.

            // Replace the game's drag result with a gentle ice deceleration.
            const float iceDrag = 0.02f; // ~2% velocity lost per fixed frame
            Vector3 afterDrag = GetVelocity(__instance);
            SetVelocity(__instance, new Vector3(
                prevVel.x * (1f - iceDrag),
                afterDrag.y,   // preserve vertical component (gravity etc.)
                prevVel.z * (1f - iceDrag)
            ));
        }

        private static Vector3 GetVelocity(object instance)
        {
            if (VelocityProp  != null) return (Vector3)VelocityProp.GetValue(instance);
            if (VelocityField != null) return (Vector3)VelocityField.GetValue(instance);
            return Vector3.zero;
        }

        private static void SetVelocity(object instance, Vector3 value)
        {
            if (VelocityProp  != null) VelocityProp.SetValue(instance, value);
            else VelocityField?.SetValue(instance, value);
        }
    }
}
