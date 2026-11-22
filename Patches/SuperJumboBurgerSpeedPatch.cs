using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using IssaPlugin.Items;

namespace IssaPlugin.Patches
{
    /// <summary>
    /// Scales a Super Jumbo Burger giant's movement speed with its size.
    ///
    /// The base game multiplies the giant's target speed by a single constant,
    /// JumboBurgerGiantFormSpeedBoostFactor, tuned for its own 3x form. That constant
    /// has no scale term, so a 10x giant covers ground at the same rate as a 3x one
    /// while having far longer legs — which reads as sluggish.
    ///
    /// WHY THE TARGET FUNCTIONS AND NOT UpdateMovementSpeed:
    ///
    ///   UpdateMovementSpeed ends with
    ///       this.speed = LerpClamped(this.speed, target, accel * fixedDeltaTime)
    ///   so `speed` is a persistent accumulator that is both the input and the output of
    ///   its own smoothing. Multiplying it in a Postfix — as an earlier version of this
    ///   patch did — feeds the multiplied value back in as the next frame's starting
    ///   point, compounding every frame: speed * 1.65 * 1.65 * 1.65... The player
    ///   accelerates without bound and is flung off the map, which shows up in the log
    ///   as "Could not find terrain for world point" at coordinates that roughly double
    ///   each line.
    ///
    ///   GetTargetSpeedOnGround / GetTargetSpeedInAir return a freshly computed TARGET
    ///   each call, derived from settings rather than from the previous result. Scaling
    ///   those is idempotent: the lerp then eases `speed` toward the higher target and
    ///   settles there, which is exactly the intended behaviour.
    ///
    /// Both functions are compiler-generated local functions of UpdateMovementSpeed, so
    /// they are targeted by their exact mangled names. If a game update renames them the
    /// patch reports it once and disables itself rather than failing silently.
    /// </summary>
    [HarmonyPatch]
    static class SuperJumboBurgerSpeedPatch
    {
        private static MethodBase _onGround;
        private static MethodBase _inAir;

        static bool Prepare()
        {
            var methods = AccessTools.GetDeclaredMethods(typeof(PlayerMovement));
            _onGround = methods.Find(m => m.Name.Contains("GetTargetSpeedOnGround"));
            _inAir = methods.Find(m => m.Name.Contains("GetTargetSpeedInAir"));

            if (_onGround == null || _inAir == null)
            {
                IssaPluginPlugin.Log.LogWarning(
                    "[SuperJumboBurger] Movement target-speed methods not found "
                        + $"(onGround={_onGround != null}, inAir={_inAir != null}) — giant "
                        + "movement speed will not scale with size. A game update likely "
                        + "renamed them."
                );
                return false;
            }

            return true;
        }

        static IEnumerable<MethodBase> TargetMethods()
        {
            yield return _onGround;
            yield return _inAir;
        }

        /// Scales the freshly computed target speed. __result is a new value each call,
        /// never a running total, so this cannot compound.
        static void Postfix(PlayerMovement __instance, ref float __result)
        {
            // PlayerInfo is assigned in Awake, so it can be null on a player's first
            // ticks. This runs every physics tick for every player, so an unguarded
            // dereference would throw once per tick per player.
            if (__instance == null)
                return;

            var info = __instance.PlayerInfo;
            if (info == null || !info.IsInJumboBurgerGiantForm)
                return;

            // Applied for every player's PlayerMovement, not just the local one, so
            // remote giants move at the same speed on every client. The multiplier is
            // derived purely from GetEffectiveCharacterScale(), a SyncVar, so every client computes
            // the same value with no extra replication.
            __result *= SuperJumboBurgerBehaviour.GetSpeedMultiplier(__instance.GetEffectiveCharacterScale());
        }
    }
}
