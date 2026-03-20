using System.Collections.Generic;
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
    ///   • Melee swings (golf club / baseball bat): after OnFinishedSwinging on the
    ///     server, OverlapSphere around the swing hitbox centre to check for bears.
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
    /// After any golf swing completes on the server, check for bears within
    /// <see cref="Configuration.BearMeleeHitRange"/> of the swing hitbox centre
    /// and apply one hit to each.
    ///
    /// Weapon detection:
    ///   • Baseball bat (BatItem.BatItemType): applies BearDamageBaseballBat damage
    ///     and BearBatKnockbackForce impulse.
    ///   • Golf club (anything else): applies BearDamageGolfClub damage and
    ///     BearMeleeKnockbackForce impulse.
    ///
    /// The knockback direction is away from the swinging player plus a slight upward
    /// angle, giving the bear the same "going flying" feel as a player hit.
    ///
    /// A HashSet prevents double-hitting the same bear from compound colliders.
    /// </summary>
    [HarmonyPatch(typeof(PlayerGolfer), "OnFinishedSwinging")]
    static class GolfClubBearHitPatch
    {
        private static readonly HashSet<GameObject> _hitBears = new();

        static void Postfix(PlayerGolfer __instance)
        {
            if (!NetworkServer.active)
                return;

            var playerInfo = __instance.PlayerInfo;
            if (playerInfo == null)
                return;

            bool isBat =
                playerInfo.Inventory?.GetEffectivelyEquippedItem(true) == BatItem.BatItemType;

            float damage = isBat
                ? Configuration.BearDamageBaseballBat.Value
                : Configuration.BearDamageGolfClub.Value;

            float knockbackForce = isBat
                ? Configuration.BearBatKnockbackForce.Value
                : Configuration.BearMeleeKnockbackForce.Value;

            // World-space centre of the swing hitbox
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

            _hitBears.Clear();

            foreach (var col in colliders)
            {
                var receiver = col.GetComponentInParent<BearHitReceiver>();
                if (receiver == null)
                    continue;

                if (!_hitBears.Add(receiver.gameObject))
                    continue;

                // Knockback: away from the swinging player + slight upward angle
                Vector3 knockDir = (
                    receiver.transform.position - playerInfo.transform.position
                ).normalized;
                knockDir = (knockDir + Vector3.up * 0.5f).normalized;

                BearExplosionAttackerContext.CurrentAttacker = playerInfo;
                receiver.Behaviour?.ApplyMeleeKnockback(knockDir, knockbackForce);
                receiver.DealDamage(damage);
                BearExplosionAttackerContext.CurrentAttacker = null;

                // Blood splatter on all clients: origin is the swinging player,
                // hit point is the bear's approximate chest position.
                NetworkServer.SendToAll(
                    new BearHitVfxMessage
                    {
                        HitPoint = receiver.transform.position + Vector3.up * 1f,
                        AttackerOrigin = playerInfo.transform.position,
                    }
                );

                IssaPluginPlugin.Log.LogInfo(
                    $"[Bear] Hit by {(isBat ? "baseball bat" : "golf club")} "
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
