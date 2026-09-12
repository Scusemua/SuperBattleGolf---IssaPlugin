using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

namespace IssaPlugin.Items
{
    /// <summary>
    /// Client-side firing logic for the Golf Cart Launcher.
    ///
    /// The launcher itself is aimed entirely on the shooter's client (mirrors the
    /// AK-47 fire loop), but the projectile is a real networked base-game golf cart,
    /// so the actual spawn is a server operation: each shot sends a
    /// GolfCartLaunchRequestMessage and GolfCartLauncherNetworkBridge spawns +
    /// launches the cart server-side.
    ///
    /// Aiming mirrors the base game's rocket launcher
    /// (PlayerInventory.ShootRocketLauncherRoutine): the aim point comes from
    /// GetFirearmAimPoint along the launcher barrel, and the player is snapped to
    /// the camera when the shot would be more than 45 degrees off their facing.
    /// </summary>
    public static class GolfCartLauncherItem
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
        public static void ResetFiringState() => _isFiring = false;

        /// <summary>
        /// Fires carts while the button is held, one every FireRate seconds.
        /// Started by GolfCartLauncherItemDefinition.OnUse on button-down.
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

            var bridge = inventory.GetComponent<GolfCartLauncherNetworkBridge>();

            try
            {
                do
                {
                    if (
                        inventory.GetEffectivelyEquippedItem(true)
                        != ItemRegistry.GolfCartLauncherItemType
                    )
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
                    Fire(inventory, bridge);
                    ItemHelper.DecrementAndRemove(inventory, slot);

                    // Stop once uses are exhausted (the item left the inventory).
                    if (
                        inventory.GetEffectivelyEquippedItem(true)
                        != ItemRegistry.GolfCartLauncherItemType
                    )
                        break;

                    yield return new WaitForSeconds(ModConfig.GolfCartLauncher.FireRate.Value);
                } while (Mouse.current != null && Mouse.current.leftButton.isPressed);
            }
            finally
            {
                ItemHelper.SetCurrentItemUse(inventory, ItemUseType.None);
                _isFiring = false;
            }
        }

        // ── Single cart launch ───────────────────────────────────────────────────

        private static void Fire(PlayerInventory inventory, GolfCartLauncherNetworkBridge bridge)
        {
            Vector3 barrelEnd = inventory.GetRocketLauncherBarrelFrontEndPosition();
            Vector3 barrelForward = inventory.GetRocketLauncherBarrelForward();
            float maxAimDist = ModConfig.GolfCartLauncher.MaxAimingDistance.Value;

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

            Vector3 dir = (aimPoint - barrelEnd).RandomlyRotatedDeg(
                ModConfig.GolfCartLauncher.Inaccuracy.Value
            );

            if (dir.sqrMagnitude < 0.0001f)
                dir = barrelForward;

            ScreenShakeHelper.ApplyScreenShake(
                ModConfig.GolfCartLauncher.ScreenShakeIntensity.Value
            );

            // The cart is a networked object — only the server may spawn it.
            bridge?.ClientRequestLaunch(dir.normalized);
        }
    }
}
