using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

namespace IssaPlugin.Items
{
    public class CannonItem
    {
        // Guards against a second coroutine starting if OnUse fires while already
        // firing (the game's input buffer can retry TryUseItem). Static because only
        // one local player fires at a time.
        private static bool _isFiring;

        /// <summary>True while the local player is mid burst.</summary>
        public static bool IsFiring => _isFiring;

        /// <summary>
        /// Clears the firing flag. Called from hole cleanup so a hole transition
        /// mid-burst cannot leave the loop permanently blocked.
        /// </summary>
        public static void ResetFiringState()
        {
            _isFiring = false;
        }

        /// <summary>
        /// Fires cannon balls while the button is held, one every FireRate seconds.
        /// Started by Cannon.OnUse on button-down.
        /// </summary>
        public static IEnumerator FireLoop(PlayerInventory inventory)
        {
            // The input buffer can retry the use while the button is already released.
            if (Mouse.current == null || !Mouse.current.leftButton.isPressed)
                yield break;

            // Guards against a second concurrent burst. The flag is also cleared by
            // ClientHoleCleanup and in the finally below, so a burst that is interrupted
            // cannot leave firing permanently blocked.
            if (_isFiring)
                yield break;

            _isFiring = true;
            ItemHelper.SetCurrentItemUse(inventory, ItemUseType.Regular);

            var bridge = inventory.GetComponent<CannonNetworkBridge>();

            try
            {
                do
                {
                    if (inventory.GetEffectivelyEquippedItem(true) != ItemRegistry.CannonItemType)
                        break;

                    // Releasing the aim button mid-burst stops the burst. TryUseItem only
                    // gates the first shot, so without this the loop would keep firing
                    // from the hip once it had started.
                    //
                    // This reads the aim INPUT, not IsAimingItem: SetCurrentItemUse above
                    // makes IsUsingItemAtAll true, and ShouldAim returns false whenever an
                    // item is in use unless it is the Railgun or ElephantGun. Our item maps
                    // to RocketLauncher, so IsAimingItem goes false the moment firing starts
                    // and testing it here would end the burst before the first shot.
                    //
                    // PlayerInput.IsHoldingAimSwing rather than Mouse.rightButton so a
                    // rebound aim control still works.
                    if (!(inventory.PlayerInfo?.Input?.IsHoldingAimSwing ?? false))
                        break;

                    int slot = inventory.EquippedItemIndex;

                    DoShoot(inventory, bridge, slot);

                    ItemHelper.DecrementAndRemove(inventory, slot);

                    // Stop once uses are exhausted (the item left the inventory).
                    if (inventory.GetEffectivelyEquippedItem(true) != ItemRegistry.CannonItemType)
                        break;

                    yield return new WaitForSeconds(ModConfig.Cannon.FireRate.Value);
                } while (Mouse.current != null && Mouse.current.leftButton.isPressed);
            }
            finally
            {
                ItemHelper.SetCurrentItemUse(inventory, ItemUseType.None);
                _isFiring = false;
            }
        }

        // ── Single cannon ball launch ───────────────────────────────────────────────────

        private static void DoShoot(
            PlayerInventory inventory,
            CannonNetworkBridge bridge,
            int equippedSlotIndex
        )
        {
            Vector3 barrelEnd = inventory.GetRocketLauncherBarrelFrontEndPosition();
            Vector3 barrelForward = inventory.GetRocketLauncherBarrelForward();
            float maxAimDist = 500f;

            Vector3 aimPoint = inventory.GetFirearmAimPoint(
                barrelEnd,
                barrelForward,
                maxAimDist,
                GameManager.LayerSettings.RocketHittablesMask,
                out float localYaw
            );

            // Same correction the base rocket launcher applies: if the shot is far off
            // the player's facing, snap them to the camera and re-resolve the aim point.
            if (Mathf.Abs(localYaw) > 45f)
            {
                inventory.PlayerInfo.Movement.AlignWithCameraImmediately();
                aimPoint = inventory.GetFirearmAimPoint(
                    barrelEnd,
                    barrelForward,
                    maxAimDist,
                    GameManager.LayerSettings.GunHittablesMask,
                    out _
                );
            }

            Vector3 dir = aimPoint - barrelEnd;

            if (dir.sqrMagnitude < 0.0001f)
                dir = barrelForward;

            ScreenShakeHelper.ApplyScreenShake(
                ModConfig.Cannon.ScreenShakeIntensity.Value
            );

            ItemHelper.PlayRocketLauncherFireEffects(inventory);

            // The cannon ball is a networked object — only the server may spawn it.
            bridge?.ClientRequestLaunch(dir.normalized, equippedSlotIndex);
        }
    }
}
