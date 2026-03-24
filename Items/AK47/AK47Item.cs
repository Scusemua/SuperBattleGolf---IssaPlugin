using System.Collections;
using System.Reflection;
using UnityEngine;
using UnityEngine.InputSystem;

namespace IssaPlugin.Items
{
    /// <summary>
    /// Sub-machine gun — rapid-fire while the fire button is held.
    /// OnUse (triggered once on button-down) starts a coroutine that loops every
    /// FireRate seconds while left-click is held. Each iteration consumes one use.
    /// When uses hit 0 the item removes itself; releasing the button exits early.
    ///
    /// Local-only — no NetworkBridge needed.
    /// ItemType 114.
    /// </summary>
    public static class AK47Item
    {
        // Prevents a second coroutine starting if OnUse is called while already firing
        // (e.g. if the game re-calls TryUseItem while held).
        private static bool _isFiring;

        // The server's elephant-gun VFX rate limiter rejects calls faster than 0.25 s
        // and kicks after 10 violations. We track the last VFX send time and skip the
        // call when it would arrive too soon — same pattern used for the shot sound.
        private static float _lastVfxTime = float.MinValue;
        private const float VfxMinInterval = 0.3f; // slightly above server's 0.25 s threshold

        // ── Reflected private methods ────────────────────────────────────────────

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

        // ── Fire loop coroutine ──────────────────────────────────────────────────

        /// <summary>
        /// Started by OnUse on button-down. Fires immediately, then continues firing
        /// every FireRate seconds while the left mouse button remains held.
        /// </summary>
        public static IEnumerator FireLoop(PlayerInventory inventory)
        {
            if (_isFiring)
                yield break;

            _isFiring = true;
            ItemHelper.SetCurrentItemUse(inventory, ItemUseType.Regular);

            // Play the shot sound once per trigger pull — calling it per-bullet exceeds
            // the game's rate limiter for the elephant gun shot sound and flags cheating.
            inventory.PlayerInfo.PlayerAudio.PlayElephantGunShotForAllClients();

            do
            {
                int slot = inventory.EquippedItemIndex;
                DoShoot(inventory);
                ItemHelper.DecrementAndRemove(inventory, slot);

                // Stop if uses were exhausted (item removed from inventory).
                if (inventory.GetEffectivelyEquippedItem(true) != ItemRegistry.AK47ItemType)
                    break;

                yield return new WaitForSeconds(Configuration.AK47FireRate.Value);

            } while (Mouse.current != null && Mouse.current.leftButton.isPressed);

            ItemHelper.SetCurrentItemUse(inventory, ItemUseType.None);
            _isFiring = false;
        }

        // ── Single bullet raycast ────────────────────────────────────────────────

        private static void DoShoot(PlayerInventory inventory)
        {
            Vector3 barrelEnd = inventory.GetElephantGunBarrelEndPosition();

            float maxAimDist = Configuration.AK47MaxAimingDistance.Value;
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

            float inaccuracy = Configuration.AK47Inaccuracy.Value;
            Vector3 dir = (aimPoint - barrelEnd).RandomlyRotatedDeg(inaccuracy);
            Ray ray = new Ray(barrelEnd, dir);

            ItemHelper.ApplyRecoil(inventory, dir, Configuration.AK47Recoil.Value);

            float maxShot = Configuration.AK47MaxShotDistance.Value;
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
                if (Time.time - _lastVfxTime >= VfxMinInterval)
                {
                    _lastVfxTime = Time.time;
                    VfxManager.PlayElephantGunMissForAllClients(inventory, ray.direction);
                }
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
                    if (Time.time - _lastVfxTime >= VfxMinInterval)
                    {
                        _lastVfxTime = Time.time;
                        VfxManager.PlayElephantGunHitForAllClients(
                            inventory,
                            new VfxManager.GunShotHitVfxData(
                                hittable,
                                true,
                                localHitPoint,
                                raycastHit.point
                            )
                        );
                    }
                    return;
                }

                var useId = (ItemUseId)(
                    IncrementAndGetCurrentItemUseIdMethod?.Invoke(
                        inventory,
                        new object[] { ItemRegistry.AK47ItemType }
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

            if (Time.time - _lastVfxTime >= VfxMinInterval)
            {
                _lastVfxTime = Time.time;
                VfxManager.PlayElephantGunHitForAllClients(
                    inventory,
                    new VfxManager.GunShotHitVfxData(hittable, false, localHitPoint, raycastHit.point)
                );
            }
        }
    }
}
