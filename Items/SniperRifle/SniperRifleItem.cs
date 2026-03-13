using System.Collections;
using System.Reflection;
using HarmonyLib;
using IssaPlugin.Overlays;
using UnityEngine;

namespace IssaPlugin.Items
{
    /// <summary>
    /// Sniper rifle — fires like the ElephantGun but without the backwards-knockback
    /// dive that InformShotElephantGun() triggers.  Right-click zooms the camera and
    /// shows the scope overlay handled by SniperScopeOverlay.
    ///
    /// ItemType 106.  No NetworkBridge needed: the shoot coroutine runs entirely on
    /// the local client (same pattern as StealthBomberItem.BomberRunRoutine).
    /// </summary>
    public static class SniperRifleItem
    {
        public static readonly ItemType SniperRifleItemType = (ItemType)106;

        /// <summary>True on the local client while the scope is being held (right-click).</summary>
        public static bool IsScoped { get; set; }

        public static void GiveSniperRifleToLocalPlayer()
        {
            ItemHelper.GiveItemToLocalPlayer(
                SniperRifleItemType,
                (int)Configuration.SniperRifleUses.Value,
                "SniperRifle"
            );
        }

        // ── Reflected private/internal methods on PlayerInventory ────────────

        private static readonly MethodInfo TryParseFirearmRaycastResultsMethod =
            typeof(PlayerInventory).GetMethod(
                "TryParseFirearmRaycastResults",
                BindingFlags.NonPublic | BindingFlags.Instance
            );

        private static readonly MethodInfo CanHitWithGunshotMethod =
            typeof(PlayerInventory).GetMethod(
                "CanHitWithGunshot",
                BindingFlags.NonPublic | BindingFlags.Instance
            );

        private static readonly MethodInfo IncrementAndGetCurrentItemUseIdMethod =
            typeof(PlayerInventory).GetMethod(
                "IncrementAndGetCurrentItemUseId",
                BindingFlags.NonPublic | BindingFlags.Instance
            );

        // ── Shoot coroutine ──────────────────────────────────────────────────

        /// <summary>
        /// Called by TryUseItemPatch on the local client when the sniper rifle fires.
        /// Mirrors ShootElephantGunRoutine but omits InformShotElephantGun (no knockback).
        /// </summary>
        public static IEnumerator ShootRoutine(PlayerInventory inventory)
        {
            ItemHelper.SetCurrentItemUse(inventory, ItemUseType.Regular);

            DoShoot(inventory);

            int slot = inventory.EquippedItemIndex;
            ItemHelper.DecrementAndRemove(inventory, slot);

            inventory.PlayerInfo.PlayerAudio.PlayElephantGunShotForAllClients();

            float elapsed = 0f;
            float duration = Configuration.SniperRifleShotDuration.Value;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                yield return null;
            }

            ItemHelper.SetCurrentItemUse(inventory, ItemUseType.None);
        }

        // ── Core shot logic (no knockback) ───────────────────────────────────

        private static void DoShoot(PlayerInventory inventory)
        {
            // Use the same barrel-end position as the ElephantGun.
            Vector3 barrelEnd = inventory.GetElephantGunBarrelEndPosition();

            float localYaw;
            float maxAimDist = Configuration.SniperRifleMaxAimingDistance.Value;
            Vector3 aimPoint = inventory.GetFirearmAimPoint(
                maxAimDist,
                GameManager.LayerSettings.GunHittablesMask,
                out localYaw
            );

            // If yaw is too far off-centre, realign and re-query (mirrors ElephantGun behaviour).
            if (Mathf.Abs(localYaw) > 45f)
            {
                inventory.PlayerInfo.Movement.AlignWithCameraImmediately();
                aimPoint = inventory.GetFirearmAimPoint(
                    Configuration.SniperRifleMaxAimingDistance.Value,
                    GameManager.LayerSettings.GunHittablesMask,
                    out _
                );
            }

            // Apply inaccuracy (much tighter than the ElephantGun when scoped).
            float inaccuracy = SniperRifleItem.IsScoped
                ? Configuration.SniperRifleScopedInaccuracy.Value
                : Configuration.SniperRifleHipFireInaccuracy.Value;

            Vector3 dir = (aimPoint - barrelEnd).RandomlyRotatedDeg(inaccuracy);
            Ray ray = new Ray(barrelEnd, dir);

            float maxShot = Configuration.SniperRifleMaxShotDistance.Value;
            int layerMask = GameManager.LayerSettings.GunHittablesMask;
            int hitCount = Physics.RaycastNonAlloc(
                ray,
                PlayerGolfer.raycastHitBuffer,
                maxShot,
                layerMask,
                QueryTriggerInteraction.Ignore
            );

            // Use reflection to call TryParseFirearmRaycastResults.
            var args = new object[] { PlayerGolfer.raycastHitBuffer, hitCount, null, null, null };
            bool parsed = (bool)(
                TryParseFirearmRaycastResultsMethod?.Invoke(inventory, args) ?? false
            );

            if (!parsed)
            {
                VfxManager.PlayElephantGunMissForAllClients(inventory, ray.direction);
                return;
            }

            var raycastHit = (RaycastHit)args[3];
            var hittable = args[4] as Hittable;

            bool canHit =
                hittable != null
                && (bool)(
                    CanHitWithGunshotMethod?.Invoke(inventory, new object[] { hittable, null })
                    ?? false
                );

            Vector3 localHitPoint = Vector3.zero;
            if (canHit)
            {
                localHitPoint = hittable.transform.InverseTransformPoint(raycastHit.point);

                // Electromagnet shield bounce — skip for simplicity (same as ElephantGun would handle,
                // but since we call HitWithItem with a new ItemUseId the hit still registers correctly).
                if (
                    hittable.AsEntity.IsPlayer
                    && hittable.AsEntity.PlayerInfo.IsElectromagnetShieldActive
                )
                {
                    VfxManager.PlayElephantGunHitForAllClients(
                        inventory,
                        new VfxManager.GunShotHitVfxData(
                            hittable,
                            true,
                            localHitPoint,
                            raycastHit.point
                        )
                    );
                    return;
                }

                var useId = (ItemUseId)(
                    IncrementAndGetCurrentItemUseIdMethod?.Invoke(
                        inventory,
                        new object[] { SniperRifleItemType }
                    ) ?? default(ItemUseId)
                );

                hittable.HitWithItem(
                    ItemType.ElephantGun, // reuse ElephantGun hit response (knockback etc.)
                    useId,
                    localHitPoint,
                    ray.direction,
                    hittable.transform.InverseTransformPoint(barrelEnd),
                    raycastHit.distance,
                    inventory,
                    false,
                    false,
                    false
                );
            }

            VfxManager.PlayElephantGunHitForAllClients(
                inventory,
                new VfxManager.GunShotHitVfxData(hittable, false, localHitPoint, raycastHit.point)
            );
        }
    }
}
