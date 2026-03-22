using System.Collections.Generic;
using HarmonyLib;
using IssaPlugin.Items;
using Mirror;
using UnityEngine;

namespace IssaPlugin.Patches
{
    /// Detects golf club and baseball bat hits on placeable walls.
    ///
    /// Reuses the same PlayerGolfer.OnFinishedSwinging hook used for bear melee
    /// detection.  A second Harmony Postfix on the same method runs independently
    /// after the bear-hit postfix; both read the same swing hitbox position.
    ///
    /// Rocket hits are handled separately in RocketPatches.Patch_Rocket_ServerExplode.
    [HarmonyPatch(typeof(PlayerGolfer), "OnFinishedSwinging")]
    static class GolfSwingWallHitPatch
    {
        private static readonly HashSet<GameObject> _hitWalls = new();

        static void Postfix(PlayerGolfer __instance)
        {
            if (!NetworkServer.active)
                return;

            var playerInfo = __instance.PlayerInfo;
            if (playerInfo == null)
                return;

            bool isBat =
                playerInfo.Inventory?.GetEffectivelyEquippedItem(true)
                == ItemRegistry.BaseballBatItemType;

            Vector3 swingCenter = __instance.transform.TransformPoint(
                GameManager.GolfSettings.SwingHitBoxLocalCenter
            );

            float range = Configuration.BearMeleeHitRange.Value;

            var colliders = Physics.OverlapSphere(
                swingCenter,
                range,
                Physics.AllLayers,
                QueryTriggerInteraction.Collide
            );

            _hitWalls.Clear();

            // TODO: Might be able to apply force to wall here.
            // foreach (var col in colliders)
            // {
            //     var receiver = col.GetComponentInParent<PlaceableWallHitReceiver>();
            //     if (receiver == null)
            //         continue;

            //     // Guard against compound colliders registering the same wall twice.
            //     if (!_hitWalls.Add(receiver.gameObject))
            //         continue;

            //     receiver.OnMeleeHit(isBat);
            // }
        }
    }
}
