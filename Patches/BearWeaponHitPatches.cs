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
    ///   • Melee swings (golf club / baseball bat): two hooks fire in sequence.
    ///     HitWithGolfSwingInternal fires at the moment the club contacts any
    ///     Hittable (e.g. the golf ball) — bears in range are hit immediately so
    ///     their reaction is in sync with the visual impact.  OnFinishedSwinging
    ///     fires afterwards as a fallback for swings aimed only at a bear (no ball
    ///     nearby), skipping any bear already hit by the first hook.
    ///     The bear is also launched with an impulse force scaled to the weapon type,
    ///     mirroring the "flying" effect players experience when hit.
    ///
    /// Both hooks run only on the server (NetworkServer.active guard) so all hit
    /// events reach BearHitReceiver, which itself requires NetworkServer.active.
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

    // ── Golf club / baseball bat ─────────────────────────────────────────────────

    /// <summary>
    /// Hits nearby bears at the moment the swing contacts any <see cref="Hittable"/>
    /// (e.g. the golf ball).  Firing here rather than at <c>OnFinishedSwinging</c>
    /// eliminates the delay between the visual impact and the bear's reaction.
    ///
    /// Bears hit here are recorded in <see cref="GolfClubBearHitPatch._earlyHitBears"/>
    /// so the fallback <c>OnFinishedSwinging</c> patch does not double-hit them.
    /// </summary>
    [HarmonyPatch]
    static class GolfSwingImpactBearHitPatch
    {
        static MethodBase TargetMethod() =>
            AccessTools.Method(typeof(Hittable), "HitWithGolfSwingInternal");

        static void Postfix(PlayerGolfer hitter)
        {
            if (!NetworkServer.active || hitter == null)
                return;

            BearMeleeHitHelper.HitBearsInSwingRange(
                hitter,
                skipSet: null,
                recordSet: GolfClubBearHitPatch._earlyHitBears,
                label: "golf club (on-impact)"
            );
        }
    }

    /// <summary>
    /// Fallback bear hit check that fires when the swing animation completes.
    /// Handles the case where the player swings at a bear without any
    /// <see cref="Hittable"/> nearby (no golf ball), so
    /// <see cref="GolfSwingImpactBearHitPatch"/> never fires.
    ///
    /// Skips bears already recorded by <see cref="GolfSwingImpactBearHitPatch"/>
    /// to prevent double-hits, then clears the record for the next swing.
    /// </summary>
    [HarmonyPatch(typeof(PlayerGolfer), "OnFinishedSwinging")]
    static class GolfClubBearHitPatch
    {
        // Bears hit during GolfSwingImpactBearHitPatch for the current swing.
        // Static is safe: Unity's physics loop is single-threaded.
        internal static readonly HashSet<GameObject> _earlyHitBears = new();

        static void Postfix(PlayerGolfer __instance)
        {
            if (!NetworkServer.active)
                return;

            BearMeleeHitHelper.HitBearsInSwingRange(
                __instance,
                skipSet: _earlyHitBears,
                recordSet: null,
                label: "golf club (fallback)"
            );

            _earlyHitBears.Clear();
        }
    }

    // ── Shared melee-hit logic ────────────────────────────────────────────────

    static class BearMeleeHitHelper
    {
        private static readonly HashSet<GameObject> _compoundDedup = new();

        /// <summary>
        /// OverlapSphere around the swing hitbox centre and apply one hit per bear.
        /// </summary>
        /// <param name="swinger">The player performing the swing.</param>
        /// <param name="skipSet">Bears to skip (already hit this swing). May be null.</param>
        /// <param name="recordSet">Set to add hit bears to. May be null.</param>
        /// <param name="label">Log label distinguishing on-impact vs fallback hits.</param>
        internal static void HitBearsInSwingRange(
            PlayerGolfer swinger,
            HashSet<GameObject> skipSet,
            HashSet<GameObject> recordSet,
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

            float range = Configuration.BearMeleeHitRange.Value;

            var colliders = Physics.OverlapSphere(
                swingCenter,
                range,
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

                recordSet?.Add(receiver.gameObject);

                Vector3 knockDir = (
                    receiver.transform.position - playerInfo.transform.position
                ).normalized;
                knockDir = (knockDir + Vector3.up * 0.5f).normalized;

                // DealDamage first: it calls OnHitByExplosion → TransitionTo(Stunned)
                // → ZeroVelocity(), which clears any prior velocity.  The knockback
                // impulse is then applied on top of that clean zero so it isn't
                // immediately cancelled by the state transition.
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
