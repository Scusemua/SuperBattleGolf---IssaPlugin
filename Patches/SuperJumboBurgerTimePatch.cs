using HarmonyLib;
using IssaPlugin.Items;

namespace IssaPlugin.Patches
{
    /// <summary>
    /// Reports knockout-driven giant-form time reductions to SuperJumboBurgerBehaviour.
    ///
    /// The base game shortens a giant form when the player fails or lands a knockout, by
    /// subtracting from jumboBurgerGiantFormRemainingTime. A super form runs on its own
    /// longer clock, so it has to learn about those reductions to stay in step.
    ///
    /// This replaces an earlier approach that tried to INFER reductions by diffing that
    /// SyncVar against the value we last wrote to it. That was unreliable: the base
    /// game's own routine also decrements the field every frame, so ordinary per-frame
    /// decay had to be told apart from a real reduction by an epsilon — which meant any
    /// frame slower than the epsilon was misread as a knockout, silently cutting the
    /// form short on low-end machines.
    ///
    /// Reading the parameter directly turns that guess into the exact number the base
    /// game used.
    /// </summary>
    [HarmonyPatch(typeof(PlayerInfo), nameof(PlayerInfo.LocalPlayerReduceJumboBurgerGiantFormTime))]
    static class SuperJumboBurgerTimePatch
    {
        /// Prefix rather than Postfix: the base method can cancel the form outright when
        /// its own timer reaches zero, and we want the reduction recorded either way.
        static void Prefix(PlayerInfo __instance, float reduction)
        {
            if (__instance == null || !__instance.isLocalPlayer)
                return;

            SuperJumboBurgerBehaviour.NotifyTimeReduction(reduction);
        }
    }
}
