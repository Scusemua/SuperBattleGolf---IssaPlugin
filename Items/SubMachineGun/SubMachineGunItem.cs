using System.Collections;
using System.Reflection;
using UnityEngine;

namespace IssaPlugin.Items
{
    /// <summary>
    /// Sub-machine gun — fires a rapid burst of inaccurate bullets on use.
    /// Fires BulletCount shots in sequence with FireRate seconds between each shot.
    /// Uses the same underlying raycast logic as the SniperRifle but with high default
    /// inaccuracy and no scoping. Local-only: no NetworkBridge needed.
    ///
    /// ItemType 114.
    /// </summary>
    public static class SubMachineGunItem
    {
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

        public static IEnumerator ShootRoutine(PlayerInventory inventory)
        {
            ItemHelper.SetCurrentItemUse(inventory, ItemUseType.Regular);

            int bulletCount = (int)Configuration.SubMachineGunBulletCount.Value;
            float fireRate = Configuration.SubMachineGunFireRate.Value;

            // Play the shot sound once to signal the burst start.
            inventory.PlayerInfo.PlayerAudio.PlayElephantGunShotForAllClients();

            for (int i = 0; i < bulletCount; i++)
            {
                DoShoot(inventory);

                if (i < bulletCount - 1)
                    yield return new WaitForSeconds(fireRate);
            }

            int slot = inventory.EquippedItemIndex;
            ItemHelper.DecrementAndRemove(inventory, slot);

            ItemHelper.SetCurrentItemUse(inventory, ItemUseType.None);
        }

        private static void DoShoot(PlayerInventory inventory)
        {
            Vector3 barrelEnd = inventory.GetElephantGunBarrelEndPosition();

            float maxAimDist = Configuration.SubMachineGunMaxAimingDistance.Value;
            Vector3 aimPoint = inventory.GetFirearmAimPoint(
                maxAimDist,
                GameManager.LayerSettings.GunHittablesMask,
                out float localYaw
            );

            if (Mathf.Abs(localYaw) > 45f)
            {
                inventory.PlayerInfo.Movement.AlignWithCameraImmediately();
                aimPoint = inventory.GetFirearmAimPoint(
                    maxAimDist,
                    GameManager.LayerSettings.GunHittablesMask,
                    out _
                );
            }

            float inaccuracy = Configuration.SubMachineGunInaccuracy.Value;
            Vector3 dir = (aimPoint - barrelEnd).RandomlyRotatedDeg(inaccuracy);
            Ray ray = new Ray(barrelEnd, dir);

            float maxShot = Configuration.SubMachineGunMaxShotDistance.Value;
            int layerMask = GameManager.LayerSettings.GunHittablesMask;
            int hitCount = Physics.RaycastNonAlloc(
                ray,
                PlayerGolfer.raycastHitBuffer,
                maxShot,
                layerMask,
                QueryTriggerInteraction.Ignore
            );

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
                        new object[] { ItemRegistry.SubMachineGunItemType }
                    ) ?? default(ItemUseId)
                );

                hittable.HitWithItem(
                    ItemType.DuelingPistol, // lighter hit response per bullet
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
