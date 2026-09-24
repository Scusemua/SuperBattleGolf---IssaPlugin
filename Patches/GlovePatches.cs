using System.Linq;
using System.Reflection;
using HarmonyLib;
using IssaPlugin.Items;
using UnityEngine;

namespace IssaPlugin.Patches
{
    /// <summary>
    /// Drives the base-game reddish "not allowed" ball material while the Glove is
    /// equipped and the local player's OwnBall is within pickup range — without
    /// requiring RMB. Also suppresses that tint when the ball is out of range
    /// (so Orbital-Laser-pose aiming no longer lights up a distant OwnBall).
    /// </summary>
    [HarmonyPatch]
    static class GloveOwnedBallNotAllowedVisualsPatch
    {
        static MethodBase TargetMethod() =>
            typeof(GolfBall)
                .GetMethods(BindingFlags.NonPublic | BindingFlags.Instance)
                .FirstOrDefault(m =>
                    m.Name.Contains("ShouldOwnedBallDisplayNotAllowedVisuals")
                );

        static bool Prepare() => TargetMethod() != null;

        static void Postfix(GolfBall __instance, ref bool __result)
        {
            var local = GameManager.LocalPlayerInfo;
            if (local == null)
                return;

            // Only rewrite the decision for the local player's own ball.
            if (local.AsGolfer?.OwnBall != __instance)
                return;

            if (local.Inventory?.GetEffectivelyEquippedItem(true) != ItemRegistry.GloveItemType)
                return;

            var bridge = local.GetComponent<GloveNetworkBridge>();
            if (bridge != null && bridge.IsHolding)
            {
                __result = false;
                return;
            }

            float radius = ModConfig.Glove.PickupRadius.Value;
            float sqr = radius * radius;
            Vector3 delta = __instance.transform.position - local.transform.position;
            __result = delta.sqrMagnitude <= sqr;
        }
    }

    /// <summary>
    /// While carrying a ball with the Glove, block switching to another inventory
    /// slot. Deselect (negative index) remains allowed so consuming the Glove on
    /// release can re-equip the club. If the player somehow unequips anyway, the
    /// server drops the ball and consumes the Glove.
    /// </summary>
    [HarmonyPatch(typeof(PlayerInventory), nameof(PlayerInventory.CanSelectItemAt))]
    static class GloveBlockItemSwitchWhileHoldingPatch
    {
        static void Postfix(PlayerInventory __instance, int __0, ref bool __result)
        {
            if (!__result)
                return;

            // Allow deselect / clear so release-consume can put a club back in hand.
            if (__0 < 0)
                return;

            var glove = __instance.GetComponent<GloveNetworkBridge>();
            if (glove != null && glove.IsHolding)
                __result = false;
        }
    }
}
