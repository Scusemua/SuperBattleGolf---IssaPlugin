using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using IssaPlugin.Items;
using Mirror;
using UnityEngine;

namespace IssaPlugin.Patches
{
    /// Swings send one hit message per orb during the game's swing window.
    /// Golf club and baseball bat both use this swing. The server applies the hit.
    [HarmonyPatch(typeof(PlayerGolfer), "ReleaseSwingChargeInternal")]
    static class OrbBomberSwingPatch
    {
        static void Postfix(PlayerGolfer __instance)
        {
            if (!__instance.isLocalPlayer)
                return;

            __instance.StartCoroutine(DetectSwing(__instance));
        }

        private static IEnumerator DetectSwing(PlayerGolfer golfer)
        {
            yield return new WaitForSeconds(GameManager.GolfSettings.SwingHitStartTime);

            float hitWindow =
                GameManager.GolfSettings.SwingHitEndTime
                - GameManager.GolfSettings.SwingHitStartTime;
            float elapsed = 0f;
            var sent = new HashSet<uint>();

            while (golfer.IsSwinging && elapsed < hitWindow)
            {
                Vector3 swingCenter = golfer.transform.TransformPoint(
                    GameManager.GolfSettings.SwingHitBoxLocalCenter
                );

                var colliders = Physics.OverlapSphere(
                    swingCenter,
                    OrbBomberBehaviour.SwingOverlapRadius,
                    Physics.AllLayers,
                    QueryTriggerInteraction.Collide
                );

                foreach (var col in colliders)
                {
                    var setup = col.GetComponentInParent<OrbBomberClientSetup>();
                    if (setup == null)
                        continue;

                    var identity = setup.GetComponent<NetworkIdentity>();
                    if (identity == null || !sent.Add(identity.netId))
                        continue;

                    NetworkClient.Send(new OrbBomberSwingHitMessage { OrbNetId = identity.netId });
                }

                elapsed += Time.deltaTime;
                yield return null;
            }
        }
    }

    /// Any rocket blast, including scale-1 shots, knocks orbs in range.
    /// Runs before the scaled-explosion patch unregisters the rocket's scale.
    [HarmonyPatch(typeof(Rocket), "ServerExplode")]
    [HarmonyPriority(Priority.High)]
    static class OrbBomberExplosionPatch
    {
        static void Postfix(Rocket __instance, Vector3 worldPosition)
        {
            if (!NetworkServer.active || __instance == null)
                return;

            float scale = Mathf.Max(1f, ExplosionScaler.GetScale(__instance));
            float range = GameManager.ItemSettings.RocketExplosionRange * scale;
            var orbs = Object.FindObjectsByType<OrbBomberBehaviour>(FindObjectsSortMode.None);
            foreach (var orb in orbs)
            {
                if (orb == null)
                    continue;
                if ((orb.transform.position - worldPosition).sqrMagnitude > range * range)
                    continue;
                orb.ApplyExplosion(worldPosition, range, scale);
            }
        }
    }
}
