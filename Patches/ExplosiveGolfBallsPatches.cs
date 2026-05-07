using System.Reflection;
using HarmonyLib;
using IssaPlugin.Items;
using Mirror;
using UnityEngine;

namespace IssaPlugin.Patches
{
    /// <summary>
    /// Prefix+Postfix on Hittable.HitWithGolfSwingInternal (server-side only).
    ///
    /// When the hitter's inventory contains Explosive Golf Balls, one use is consumed
    /// and ExplosiveGolfBallsBehaviour is attached to the ball.  That component fires
    /// the explosion on the ball's first collision.
    /// </summary>
    [HarmonyPatch]
    static class ExplosiveGolfBallsSwingPatch
    {
        static MethodBase TargetMethod() =>
            AccessTools.Method(typeof(Hittable), "HitWithGolfSwingInternal");

        // Captured in Prefix before HitWithGolfSwingInternal has a chance to clear LockOnTarget.
        private static bool _pendingIsHoming;

        static void Prefix(Hittable __instance, PlayerGolfer hitter)
        {
            _pendingIsHoming = false;
            if (!NetworkServer.active)
            {
                IssaPluginPlugin.Log.LogInfo("[ExplosiveGolfBalls] Prefix: skipped — not server.");
                return;
            }
            if (!__instance.AsEntity.IsGolfBall)
            {
                IssaPluginPlugin.Log.LogInfo("[ExplosiveGolfBalls] Prefix: skipped — not a golf ball.");
                return;
            }
            _pendingIsHoming = hitter?.LockOnTarget != null;
            IssaPluginPlugin.Log.LogInfo(
                $"[ExplosiveGolfBalls] Prefix: isGolfBall=true hitter={hitter?.name} "
                + $"LockOnTarget={hitter?.LockOnTarget?.name ?? "null"} pendingIsHoming={_pendingIsHoming}"
            );
        }

        static void Postfix(Hittable __instance, PlayerGolfer hitter)
        {
            if (!NetworkServer.active)
                return;
            if (hitter == null)
            {
                IssaPluginPlugin.Log.LogInfo("[ExplosiveGolfBalls] Postfix: skipped — hitter is null.");
                return;
            }
            if (!__instance.AsEntity.IsGolfBall)
                return;

            var inventory = hitter.PlayerInfo?.Inventory;
            if (inventory == null)
            {
                IssaPluginPlugin.Log.LogInfo("[ExplosiveGolfBalls] Postfix: skipped — inventory is null.");
                return;
            }

            int slot = ItemRegistry.FindSlotIndex(
                inventory,
                ItemRegistry.ExplosiveGolfBallsItemType
            );

            IssaPluginPlugin.Log.LogInfo(
                $"[ExplosiveGolfBalls] Postfix: hitter={hitter.name} slot={slot} "
                + $"pendingIsHoming={_pendingIsHoming} lockOnOnly={ModConfig.ExplosiveGolfBalls.LockOnOnly.Value}"
            );

            if (slot < 0)
            {
                IssaPluginPlugin.Log.LogInfo("[ExplosiveGolfBalls] Postfix: skipped — item not in inventory.");
                return;
            }

            if (ModConfig.ExplosiveGolfBalls.LockOnOnly.Value && !_pendingIsHoming)
            {
                IssaPluginPlugin.Log.LogInfo("[ExplosiveGolfBalls] Postfix: skipped — LockOnOnly=true but shot was not homing.");
                return;
            }

            if (__instance.GetComponent<ExplosiveGolfBallsBehaviour>() != null)
            {
                IssaPluginPlugin.Log.LogInfo("[ExplosiveGolfBalls] Postfix: skipped — behaviour already attached.");
                return;
            }

            ItemHelper.ConsumeItemAtSlot(inventory, slot);

            var behaviour = __instance.gameObject.AddComponent<ExplosiveGolfBallsBehaviour>();
            behaviour.Hitter = hitter;
            behaviour.ExplosionScale = ModConfig.ExplosiveGolfBalls.ExplosionScale.Value;

            IssaPluginPlugin.Log.LogInfo(
                $"[ExplosiveGolfBalls] Postfix: behaviour attached. scale={behaviour.ExplosionScale}"
            );
        }
    }
}
