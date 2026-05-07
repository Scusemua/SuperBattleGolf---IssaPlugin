using System.Collections.Generic;
using HarmonyLib;
using IssaPlugin.Items;
using Mirror;
using UnityEngine;

namespace IssaPlugin.Patches
{
    /// Makes dropped custom items hittable by the golf club and baseball bat.
    ///
    /// DroppedCustomItem has no Hittable component, so the base game's swing
    /// detection (which calls TryGetComponentInParent<Hittable>) silently skips
    /// them.  This postfix adds a secondary check on the server after the normal
    /// hit pipeline, identical in structure to GolfSwingWallHitPatch.
    // [HarmonyPatch(typeof(PlayerGolfer), "OnFinishedSwinging")]
    // static class DroppedItemSwingHitPatch
    // {
    //     private const float KnockbackForce = 12f;
    //     private const float UpwardBias = 0.3f;

    //     private static readonly HashSet<GameObject> _hitItems = new();

    //     static void Postfix(PlayerGolfer __instance)
    //     {
    //         if (!NetworkServer.active)
    //             return;

    //         var playerInfo = __instance.PlayerInfo;
    //         if (playerInfo == null)
    //             return;

    //         Vector3 swingCenter = __instance.transform.TransformPoint(
    //             GameManager.GolfSettings.SwingHitBoxLocalCenter
    //         );

    //         var colliders = Physics.OverlapSphere(
    //             swingCenter,
    //             ModConfig.Bear.MeleeHitRange.Value,
    //             Physics.AllLayers,
    //             QueryTriggerInteraction.Collide
    //         );

    //         _hitItems.Clear();

    //         foreach (var col in colliders)
    //         {
    //             var dropped = col.GetComponentInParent<DroppedCustomItem>();
    //             if (dropped == null)
    //                 continue;

    //             if (!_hitItems.Add(dropped.gameObject))
    //                 continue;

    //             var rb = dropped.GetComponent<Rigidbody>();
    //             if (rb == null || rb.isKinematic)
    //                 continue;

    //             Vector3 dir = (
    //                 dropped.transform.position - playerInfo.transform.position
    //             ).normalized;
    //             dir = (dir + Vector3.up * UpwardBias).normalized;

    //             rb.AddForce(dir * KnockbackForce, ForceMode.Impulse);
    //         }
    //     }
    // }
}
