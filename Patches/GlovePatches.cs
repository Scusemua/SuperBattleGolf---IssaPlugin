using System.Linq;
using System.Reflection;
using HarmonyLib;
using IssaPlugin.Items;
using IssaPlugin.Overlays;
using UnityEngine;

namespace IssaPlugin.Patches
{
    /// <summary>
    /// Drives the base-game reddish "not allowed" ball material for Glove
    /// (OwnBall in pickup range, no RMB) and Evil Glove (aim-locked ball only).
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

            var equipped = local.Inventory?.GetEffectivelyEquippedItem(true);
            var bridge = local.GetComponent<GloveNetworkBridge>();

            if (equipped == ItemRegistry.GloveItemType)
            {
                if (bridge != null && bridge.IsHolding)
                {
                    __result = false;
                    return;
                }

                float radius = ModConfig.Glove.PickupRadius.Value;
                float sqr = radius * radius;
                Vector3 delta = __instance.transform.position - local.transform.position;
                __result = delta.sqrMagnitude <= sqr;
                return;
            }

            if (equipped == ItemRegistry.EvilGloveItemType)
            {
                if (bridge != null && bridge.IsHolding)
                {
                    __result = false;
                    return;
                }

                // Tint only the aim-locked ball (reticle + tint).
                __result =
                    EvilGloveOverlay.Instance != null
                    && EvilGloveOverlay.Instance.BestTargetBall == __instance;
            }
        }
    }

    /// <summary>
    /// Evil Glove aim lock: tint another player's ball when it is the BestTarget.
    /// Also suppresses the OL-pose "all unowned balls" tint while Evil Glove is
    /// equipped so only the locked ball lights up.
    /// </summary>
    [HarmonyPatch]
    static class EvilGloveUnownedBallNotAllowedVisualsPatch
    {
        static MethodBase TargetMethod() =>
            typeof(GolfBall)
                .GetMethods(BindingFlags.NonPublic | BindingFlags.Instance)
                .FirstOrDefault(m =>
                    m.Name.Contains("ShouldUnownedBallDisplayNotAllowedVisuals")
                );

        static bool Prepare() => TargetMethod() != null;

        static void Postfix(GolfBall __instance, ref bool __result)
        {
            var local = GameManager.LocalPlayerInfo;
            if (local == null)
                return;

            if (local.Inventory?.GetEffectivelyEquippedItem(true) != ItemRegistry.EvilGloveItemType)
                return;

            var bridge = local.GetComponent<GloveNetworkBridge>();
            if (bridge != null && bridge.IsHolding)
            {
                __result = false;
                return;
            }

            __result =
                EvilGloveOverlay.Instance != null
                && EvilGloveOverlay.Instance.BestTargetBall == __instance;
        }
    }

    /// <summary>
    /// While carrying a ball with the Glove / Evil Glove, block switching to another
    /// inventory slot. Deselect (negative index) remains allowed so consuming the
    /// glove on release can re-equip the club.
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
