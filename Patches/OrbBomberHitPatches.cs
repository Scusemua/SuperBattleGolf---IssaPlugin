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
        private static readonly Collider[] SwingHits = new Collider[32];

        static void Postfix(PlayerGolfer __instance)
        {
            if (!__instance.isLocalPlayer)
                return;

            __instance.StartCoroutine(DetectSwing(__instance));
        }

        private static IEnumerator DetectSwing(PlayerGolfer golfer)
        {
            OrbBomberBehaviour.GetSwingHitWindow(golfer, out float start, out float end);
            yield return new WaitForSeconds(Mathf.Max(0f, start));

            float hitWindow = Mathf.Max(0f, end - start);
            float elapsed = 0f;
            var sent = new HashSet<uint>();

            while (golfer.IsSwinging && elapsed < hitWindow)
            {
                OrbBomberBehaviour.GetSwingProbe(golfer, out Vector3 swingCenter, out float probeRadius);

                int count = Physics.OverlapSphereNonAlloc(
                    swingCenter,
                    probeRadius,
                    SwingHits,
                    Physics.AllLayers,
                    QueryTriggerInteraction.Ignore
                );

                for (int i = 0; i < count; i++)
                {
                    var col = SwingHits[i];
                    if (col == null)
                        continue;
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

    /// Server-side swing, including a detonating orb the client probe missed.
    [HarmonyPatch(typeof(PlayerGolfer), "OnFinishedSwinging")]
    static class OrbBomberSwingFallbackPatch
    {
        private static readonly Collider[] SwingHits = new Collider[32];

        static void Postfix(PlayerGolfer __instance)
        {
            if (!NetworkServer.active || __instance == null)
                return;

            var swinger = __instance.PlayerInfo;
            if (swinger == null)
                return;

            Vector3 swingCenter;
            float probeRadius;
            OrbBomberBehaviour.GetSwingProbe(__instance, out swingCenter, out probeRadius);
            int count = Physics.OverlapSphereNonAlloc(
                swingCenter,
                probeRadius,
                SwingHits,
                Physics.AllLayers,
                QueryTriggerInteraction.Ignore
            );

            var sent = new HashSet<uint>();
            for (int i = 0; i < count; i++)
            {
                var col = SwingHits[i];
                if (col == null)
                    continue;

                var behaviour = col.GetComponentInParent<OrbBomberBehaviour>();
                if (behaviour == null)
                    continue;

                var identity = behaviour.GetComponent<NetworkIdentity>();
                if (identity == null || !sent.Add(identity.netId))
                    continue;

                behaviour.ServerHandleSwing(swinger);
            }
        }
    }

    /// Every firearm ray ends in TryParseFirearmRaycastResults. The orb is not a
    /// Hittable, so the gun ignores it unless we read the raw buffer ourselves.
    [HarmonyPatch(typeof(PlayerInventory), "TryParseFirearmRaycastResults")]
    static class OrbBomberFirearmHitPatch
    {
        static void Postfix(
            PlayerInventory __instance,
            RaycastHit[] raycastResults,
            int raycastHitCount
        )
        {
            if (!NetworkClient.active || raycastResults == null || raycastHitCount <= 0)
                return;

            float bestDistance = float.MaxValue;
            RaycastHit best = default;
            bool found = false;
            for (int i = 0; i < raycastHitCount; i++)
            {
                var hit = raycastResults[i];
                if (hit.collider == null || hit.distance >= bestDistance)
                    continue;
                bestDistance = hit.distance;
                best = hit;
                found = true;
            }

            if (!found)
                return;

            var setup = best.collider.GetComponentInParent<OrbBomberClientSetup>();
            if (setup == null)
                return;

            Vector3 origin = __instance.GetElephantGunBarrelEndPosition();
            Vector3 direction = best.point - origin;
            if (direction.sqrMagnitude < 0.0001f)
                direction = -best.normal;

            if (NetworkServer.active)
            {
                setup.GetComponent<OrbBomberBehaviour>()?.ApplyFirearmHit(direction);
                return;
            }

            var identity = setup.GetComponent<NetworkIdentity>();
            if (identity == null)
                return;

            __instance.connectionToServer?.Send(
                new OrbBomberBulletHitMessage
                {
                    OrbNetId = identity.netId,
                    Direction = direction.normalized,
                }
            );
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
                var body = orb.GetComponent<Rigidbody>();
                Vector3 center = body != null ? body.worldCenterOfMass : orb.transform.position;
                if ((center - worldPosition).sqrMagnitude > range * range)
                    continue;
                orb.ApplyExplosion(worldPosition, range, scale);
            }
        }
    }
}
