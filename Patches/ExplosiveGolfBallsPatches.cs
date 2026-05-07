using System.Reflection;
using HarmonyLib;
using IssaPlugin.Items;
using Mirror;
using UnityEngine;

namespace IssaPlugin.Patches
{
    /// <summary>
    /// Postfix on Hittable.HitWithGolfSwingInternal (server-side only).
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

        static void Postfix(Hittable __instance, PlayerGolfer hitter, Hittable homingTargetHittable)
        {
            if (!NetworkServer.active || hitter == null || !__instance.AsEntity.IsGolfBall)
            {
                return;
            }

            var inventory = hitter.PlayerInfo?.Inventory;
            if (inventory == null)
            {
                return;
            }

            int slot = ItemRegistry.FindSlotIndex(
                inventory,
                ItemRegistry.ExplosiveGolfBallsItemType
            );

            bool isHoming = homingTargetHittable != null;

            if (slot < 0 || __instance.GetComponent<ExplosiveGolfBallsBehaviour>() != null)
            {
                return;
            }

            if (ModConfig.ExplosiveGolfBalls.LockOnOnly.Value && !isHoming)
            {
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
