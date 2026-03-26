using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using IssaPlugin.Items;
using Mirror;
using UnityEngine;

namespace IssaPlugin.Patches
{
    /// <summary>
    /// Makes bears hittable by the elephant gun, dueling pistol, golf club, and
    /// baseball bat.
    ///
    /// Bears have no <see cref="Hittable"/> component, so the game's standard hit
    /// detection silently skips them.  These patches add a secondary bear-specific
    /// check after the normal hit pipeline:
    ///
    ///   • Gun shots (elephant gun / dueling pistol): when a shot finds no player
    ///     (PlayElephantGunMissForAllClients / PlayDuelingPistolMissForAllClients),
    ///     re-cast the same ray against all layers and check for BearHitReceiver.
    ///
    ///   • Melee swings (golf club / baseball bat): the local client runs a
    ///     coroutine during the swing hit window (SwingHitStartTime →
    ///     SwingHitEndTime) that checks for bears each frame and sends a
    ///     BearSwingHitMessage to the server on first detection.  The server
    ///     handler applies damage and knockback immediately, so the bear reacts
    ///     in sync with the visual impact rather than at OnFinishedSwinging.
    ///     OnFinishedSwinging remains as a fallback for host-only (no-client)
    ///     scenarios, and skips any bear already hit via the message this swing.
    ///
    /// Both server-side hooks guard on NetworkServer.active.
    /// </summary>
    // ── Elephant gun ─────────────────────────────────────────────────────────────

    [HarmonyPatch(typeof(VfxManager), "PlayElephantGunMissForAllClients")]
    static class ElephantGunBearHitPatch
    {
        static void Postfix(PlayerInventory shootingPlayer, Vector3 direction)
        {
            if (!NetworkServer.active || shootingPlayer == null)
                return;

            BearWeaponHitHelper.TryHitBearAlongRay(
                shootingPlayer.PlayerInfo,
                shootingPlayer.GetElephantGunBarrelEndPosition(),
                direction,
                Configuration.BearDamageElephantGun.Value
            );
        }
    }

    // ── Dueling pistol ───────────────────────────────────────────────────────────

    [HarmonyPatch(typeof(VfxManager), "PlayDuelingPistolMissForAllClients")]
    static class DuelingPistolBearHitPatch
    {
        static void Postfix(PlayerInventory shootingPlayer, Vector3 direction)
        {
            if (!NetworkServer.active || shootingPlayer == null)
                return;

            BearWeaponHitHelper.TryHitBearAlongRay(
                shootingPlayer.PlayerInfo,
                shootingPlayer.GetDuelingPistolBarrelEndPosition(),
                direction,
                Configuration.BearDamageDuelingPistol.Value
            );
        }
    }

    // ── Golf club / baseball bat — client-side hit detection ─────────────────────

    /// <summary>
    /// Patches <c>PlayerGolfer.ReleaseSwingChargeInternal</c> on the local client.
    /// Starts a per-swing coroutine that OverlapSpheres for bears each frame during
    /// the game's own swing hit window (SwingHitStartTime → SwingHitEndTime).
    /// Each unique bear found causes a <see cref="BearSwingHitMessage"/> to be sent
    /// to the server, where the hit is processed immediately rather than waiting for
    /// <c>OnFinishedSwinging</c>.
    /// </summary>
    [HarmonyPatch]
    static class BearSwingClientDetectionPatch
    {
        static MethodBase TargetMethod() =>
            AccessTools.Method(typeof(PlayerGolfer), "ReleaseSwingChargeInternal");

        static void Postfix(PlayerGolfer __instance)
        {
            if (!__instance.isLocalPlayer)
                return;

            __instance.StartCoroutine(BearSwingDetectionRoutine(__instance));
        }

        private static IEnumerator BearSwingDetectionRoutine(PlayerGolfer golfer)
        {
            // Wait until the hitbox becomes active.
            yield return new WaitForSeconds(GameManager.GolfSettings.SwingHitStartTime);

            float hitWindowDuration =
                GameManager.GolfSettings.SwingHitEndTime
                - GameManager.GolfSettings.SwingHitStartTime;

            float elapsed = 0f;

            // One-per-swing dedup: each bear's netId is added when the message is sent.
            var hitBears = new HashSet<uint>();

            while (golfer.IsSwinging && elapsed < hitWindowDuration)
            {
                Vector3 swingCenter = golfer.transform.TransformPoint(
                    GameManager.GolfSettings.SwingHitBoxLocalCenter
                );

                var colliders = Physics.OverlapSphere(
                    swingCenter,
                    Configuration.BearMeleeHitRange.Value,
                    Physics.AllLayers,
                    QueryTriggerInteraction.Collide
                );

                foreach (var col in colliders)
                {
                    var marker = col.GetComponentInParent<BearMarker>();
                    if (marker == null)
                        continue;

                    var ni = marker.GetComponent<NetworkIdentity>();
                    if (ni == null || !hitBears.Add(ni.netId))
                        continue;

                    NetworkClient.Send(new BearSwingHitMessage { BearNetId = ni.netId });
                }

                elapsed += Time.deltaTime;
                yield return null;
            }
        }
    }

    // ── Golf club / baseball bat — server-side fallback ───────────────────────────

    /// <summary>
    /// Server-side fallback: fires when the swing animation completes.
    /// Handles rare cases where the local client's <see cref="BearSwingHitMessage"/>
    /// was not sent (e.g. no client attached to the server, or the bear entered
    /// range after the hit window closed).
    ///
    /// Skips any bear that was already hit via a <see cref="BearSwingHitMessage"/>
    /// this swing (tracked per-player in <see cref="_swingHitPairs"/>), then
    /// clears those entries so the next swing starts fresh.
    /// </summary>
    [HarmonyPatch(typeof(PlayerGolfer), "OnFinishedSwinging")]
    static class GolfClubBearHitPatch
    {
        static void Postfix(PlayerGolfer __instance)
        {
            if (!NetworkServer.active)
                return;

            var playerInfo = __instance.PlayerInfo;
            if (playerInfo == null)
            {
                BearNetworkBridge.SwingHitPairs.Clear();
                return;
            }

            // Build the per-player skip set from the shared hit-pairs table.
            var skipSet = new HashSet<GameObject>();
            foreach (var pair in BearNetworkBridge.SwingHitPairs)
                if (pair.Item1 == playerInfo)
                    skipSet.Add(pair.Item2);

            BearMeleeHitHelper.HitBearsInSwingRange(
                __instance,
                skipSet: skipSet,
                label: "golf club (fallback)"
            );

            // Clear this player's entries so the next swing starts clean.
            BearNetworkBridge.SwingHitPairs.RemoveWhere(pair => pair.Item1 == playerInfo);
        }
    }

    // ── Shared melee-hit logic ────────────────────────────────────────────────

    static class BearMeleeHitHelper
    {
        private static readonly HashSet<GameObject> _compoundDedup = [];

        /// <summary>
        /// OverlapSphere around the swing hitbox centre and apply one hit per bear.
        /// </summary>
        /// <param name="swinger">The player performing the swing.</param>
        /// <param name="skipSet">Bears to skip (already hit this swing). May be null.</param>
        /// <param name="label">Log label for debugging.</param>
        internal static void HitBearsInSwingRange(
            PlayerGolfer swinger,
            HashSet<GameObject> skipSet,
            string label
        )
        {
            var playerInfo = swinger.PlayerInfo;
            if (playerInfo == null)
                return;

            bool isBat =
                playerInfo.Inventory?.GetEffectivelyEquippedItem(true)
                == ItemRegistry.BaseballBatItemType;

            float damage = isBat
                ? Configuration.BearDamageBaseballBat.Value
                : Configuration.BearDamageGolfClub.Value;

            float knockbackForce = isBat
                ? Configuration.BearBatKnockbackForce.Value
                : Configuration.BearMeleeKnockbackForce.Value;

            Vector3 swingCenter = swinger.transform.TransformPoint(
                GameManager.GolfSettings.SwingHitBoxLocalCenter
            );

            var colliders = Physics.OverlapSphere(
                swingCenter,
                Configuration.BearMeleeHitRange.Value,
                Physics.AllLayers,
                QueryTriggerInteraction.Collide
            );

            _compoundDedup.Clear();

            foreach (var col in colliders)
            {
                var receiver = col.GetComponentInParent<BearHitReceiver>();
                if (receiver == null)
                    continue;

                if (!_compoundDedup.Add(receiver.gameObject))
                    continue;

                if (skipSet != null && skipSet.Contains(receiver.gameObject))
                    continue;

                Vector3 knockDir = (
                    receiver.transform.position - playerInfo.transform.position
                ).normalized;
                knockDir = (knockDir + Vector3.up * 0.5f).normalized;

                // DealDamage first: transitions to Stunned → ZeroVelocity(), then
                // ApplyMeleeKnockback applies the impulse on top of the zero.
                BearExplosionAttackerContext.CurrentAttacker = playerInfo;
                receiver.DealDamage(damage);
                BearExplosionAttackerContext.CurrentAttacker = null;
                receiver.Behaviour?.ApplyMeleeKnockback(knockDir, knockbackForce);

                NetworkServer.SendToAll(
                    new BearHitVfxMessage
                    {
                        HitPoint = receiver.transform.position + Vector3.up * 1f,
                        AttackerOrigin = playerInfo.transform.position,
                    }
                );

                IssaPluginPlugin.Log.LogInfo(
                    $"[Bear] Hit by {(isBat ? "baseball bat" : label)} "
                        + $"from {playerInfo.PlayerId.PlayerName} for {damage} damage."
                );
            }
        }
    }

    // ── Shared helper ────────────────────────────────────────────────────────────

    static class BearWeaponHitHelper
    {
        private const float MaxGunRange = 300f;

        /// <summary>
        /// Fires a single raycast from <paramref name="origin"/> in
        /// <paramref name="direction"/> against all layers.  If the first solid
        /// hit belongs to a bear, deals <paramref name="damage"/> HP to that bear
        /// and credits <paramref name="attacker"/> for aggro.
        /// </summary>
        internal static void TryHitBearAlongRay(
            PlayerInfo attacker,
            Vector3 origin,
            Vector3 direction,
            float damage
        )
        {
            if (direction == Vector3.zero)
                return;

            if (
                !Physics.Raycast(
                    origin,
                    direction,
                    out RaycastHit hit,
                    MaxGunRange,
                    Physics.AllLayers,
                    QueryTriggerInteraction.Ignore
                )
            )
                return;

            var receiver = hit.collider.GetComponentInParent<BearHitReceiver>();
            if (receiver == null)
                return;

            BearExplosionAttackerContext.CurrentAttacker = attacker;
            receiver.DealDamage(damage);
            BearExplosionAttackerContext.CurrentAttacker = null;

            // Blood splatter on all clients: origin is the barrel end, hit point
            // is the exact raycast contact point on the bear's collider.
            NetworkServer.SendToAll(
                new BearHitVfxMessage { HitPoint = hit.point, AttackerOrigin = origin }
            );

            IssaPluginPlugin.Log.LogInfo(
                $"[Bear] Hit by gun shot from {attacker?.PlayerId.PlayerName} for {damage} damage."
            );
        }
    }
}
