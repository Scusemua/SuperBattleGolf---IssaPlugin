using System.Reflection;
using HarmonyLib;
using IssaPlugin.Items;

namespace IssaPlugin.Patches
{
    /// <summary>
    /// Shared state for the two patches below.
    ///
    /// GetSwingHitSpeed does not receive the swinger, so it is captured from its caller,
    /// HitWithGolfSwingInternal.
    ///
    /// Safe as a plain static: HitWithGolfSwingInternal is the ONLY caller of
    /// GetSwingHitSpeed (verified against the decompiled source), and it calls it
    /// exactly once, synchronously, on the main thread, with nothing in between. A swing
    /// that hits several targets calls the whole chain once per target, sequentially
    /// rather than nested, so each hit sets and clears this independently.
    /// </summary>
    static class SuperJumboBurgerFlickState
    {
        internal static PlayerGolfer CurrentHitter;
    }

    /// <summary>
    /// Captures the swinger for the duration of one swing hit.
    ///
    /// Cleared by a Finalizer rather than a Postfix: a Postfix is skipped when the
    /// patched method throws, which would leave a stale hitter behind for the next hit.
    ///
    /// This lives in its own class because Harmony forbids combining a TargetMethod()
    /// with individual [HarmonyPatch] annotations in the same class — doing so throws
    /// during PatchAll and aborts the rest of the mod's patching.
    /// </summary>
    [HarmonyPatch(typeof(Hittable), "HitWithGolfSwingInternal")]
    static class SuperJumboBurgerFlickHitterCapture
    {
        [HarmonyPrefix]
        static void Prefix(PlayerGolfer hitter) =>
            SuperJumboBurgerFlickState.CurrentHitter = hitter;

        [HarmonyFinalizer]
        static void Finalizer() => SuperJumboBurgerFlickState.CurrentHitter = null;
    }

    /// <summary>
    /// Amplifies the giant flick — the left-click swing that sends players and objects
    /// flying — while the swinger is in a Super Jumbo Burger form.
    ///
    /// It scales the RESULTING hit speed, not the incoming `power`. `power` is a
    /// normalized 0-1 swing charge that GetSwingHitSpeed feeds into BMath.Remap between
    /// the giant's min and max hit speeds. That Remap is unclamped (Remap, not
    /// RemapClamped), so multiplying `power` does have an effect — but a wildly
    /// non-linear one, because a narrow normalized band is being extrapolated across a
    /// much wider speed range. A 2x on `power` would be far more than 2x the speed, and
    /// the factor would shift if the game ever retuned those bands.
    ///
    /// Multiplying the speed instead makes the config value mean exactly what it says.
    /// Everything downstream still follows: the projectile-conversion threshold and the
    /// knockback applied to the victim are both derived from this return value.
    /// </summary>
    [HarmonyPatch]
    static class SuperJumboBurgerFlickPatch
    {
        static MethodBase TargetMethod() =>
            AccessTools.Method(typeof(Hittable), "GetSwingHitSpeed");

        static void Postfix(SwingType swingType, ref float __result)
        {
            // Only the giant flick. Ordinary swings by a giant are left alone.
            if (swingType != SwingType.JumboBurgerGiant)
                return;

            // Explicit Unity-semantics null checks rather than `?.`. The ?. operator
            // tests REFERENCE null, but a destroyed UnityEngine.Object is non-null by
            // reference while its overloaded == reports null — so a chain of ?. would
            // happily dereference a swinger that was destroyed mid-swing (a disconnect
            // between the Prefix that captured it and this Postfix).
            var hitter = SuperJumboBurgerFlickState.CurrentHitter;
            if (hitter == null)
                return;

            var info = hitter.PlayerInfo;
            if (info == null)
                return;

            var movement = info.Movement;
            if (movement == null)
                return;

            // Keyed off the swinger's replicated scale, so this resolves identically on
            // whichever client owns the target and applies to remote giants too.
            __result *= SuperJumboBurgerBehaviour.GetFlickPowerMultiplier(
                movement.CharacterScale
            );
        }
    }
}
