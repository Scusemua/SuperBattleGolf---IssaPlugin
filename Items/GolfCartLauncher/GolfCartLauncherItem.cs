using System.Collections;
using Mirror;
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

        // ── Joyride mode ─────────────────────────────────────────────────────────

        /// <summary>
        /// When true, the next shot seats the shooter in the launched cart and consumes
        /// the entire item. Local-only UI state: the server is told per shot, so this
        /// never needs syncing.
        /// </summary>
        public static bool JoyrideArmed { get; private set; }

        /// <summary>
        /// Joyride may only be armed while the equipped launcher is at full uses, since
        /// firing consumes every remaining use. Also the condition the HUD greys out on.
        /// </summary>
        public static bool CanArmJoyride(PlayerInventory inventory)
        {
            if (inventory == null)
                return false;

            if (
                inventory.GetEffectivelyEquippedItem(true)
                != ItemRegistry.GolfCartLauncherItemType
            )
                return false;

            inventory.GetUsesForSlot(
                inventory.EquippedItemIndex,
                out int remainingUses,
                out int maxUses
            );

            return maxUses > 0 && remainingUses >= maxUses;
        }

        /// <summary>
        /// Flips Joyride mode. Ignored (and disarms) when the launcher is not at full
        /// uses, so the mode can never be left armed in a state that cannot fire it.
        /// </summary>
        public static void ToggleJoyride(PlayerInventory inventory)
        {
            if (!CanArmJoyride(inventory))
            {
                JoyrideArmed = false;
                return;
            }

            JoyrideArmed = !JoyrideArmed;
        }

        /// <summary>Disarms Joyride. Called on hole cleanup and after a joyride shot.</summary>
        public static void DisarmJoyride() => JoyrideArmed = false;

        /// <summary>True while the local player is mid burst.</summary>
        public static bool IsFiring => _isFiring;

        /// <summary>
        /// Clears the firing flag. Called from hole cleanup so a hole transition
        /// mid-burst cannot leave the loop permanently blocked.
        /// </summary>
        public static void ResetFiringState()
        {
            _isFiring = false;
            JoyrideArmed = false;
        }

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

                    // Latch the mode for this shot: a Joyride shot consumes the whole
                    // item and ends the burst, so it can only ever be the first one.
                    bool joyride = JoyrideArmed && CanArmJoyride(inventory);

                    Fire(inventory, bridge, joyride, slot);

                    if (joyride)
                    {
                        // The whole item is spent riding the cart. Safe to do
                        // immediately: the server validates against the slot index sent
                        // with the launch request, not against live equipped state.
                        ConsumeWholeItem(inventory, slot);

                        // CanEnterGolfCart() refuses while IsUsingItemAtAll is true, and
                        // the server seats the player as part of handling this shot — so
                        // the use state has to be cleared now, not in the finally below.
                        ItemHelper.SetCurrentItemUse(inventory, ItemUseType.None);
                        DisarmJoyride();
                        break;
                    }

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

        /// <summary>
        /// Spends every remaining use of the launcher in <paramref name="slot"/>.
        ///
        /// The slot is re-checked each iteration rather than decrementing a count
        /// captured up front, because DecrementUseFromSlotAt does NOT guard against an
        /// empty slot: once RemoveIfOutOfUses clears it, any further call would
        /// decrement whatever occupies that slot next (into negative uses) and Cmd the
        /// server for each one.
        /// </summary>
        private static void ConsumeWholeItem(PlayerInventory inventory, int slot)
        {
            if (inventory == null)
                return;

            while (
                inventory.GetEffectivelyEquippedItem(true)
                    == ItemRegistry.GolfCartLauncherItemType
                && inventory.EquippedItemIndex == slot
            )
            {
                inventory.GetUsesForSlot(slot, out int remainingUses, out _);
                if (remainingUses <= 0)
                    break;

                ItemHelper.DecrementAndRemove(inventory, slot);
            }
        }

        // ── Single cart launch ───────────────────────────────────────────────────

        private static void Fire(
            PlayerInventory inventory,
            GolfCartLauncherNetworkBridge bridge,
            bool joyride,
            int equippedSlotIndex
        )
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

            PlayFireEffects(inventory);

            // The cart is a networked object — only the server may spawn it.
            bridge?.ClientRequestLaunch(dir.normalized, joyride, equippedSlotIndex);
        }

        // ── Firing effects ───────────────────────────────────────────────────────

        /// <summary>
        /// Plays the base game's rocket launcher muzzle flash, back blast and shot
        /// sound for every client.
        ///
        /// The two pooled VFX are the same ones VfxManager.PlayRocketLaunchLocalOnly
        /// spawns for a real rocket, positioned from the launcher's own barrel helpers
        /// (which this item already uses for aiming), so they line up with the weapon
        /// without any manual placement.
        ///
        /// Broadcasting has to pick the right entry point for where this runs:
        ///   - On a pure client, ClientPlayPooledVfxForAllClients plays locally and
        ///     Cmds the rest. Calling it on the server instead LOGS AN ERROR AND DOES
        ///     NOTHING ("On the server, ServerPlayPooledVfxForAllClients should be
        ///     called instead"), which is why the host saw no effect at all.
        ///   - On the host, ServerPlayPooledVfxForAllClients RPCs the other clients but
        ///     does not play locally, so the host also plays it itself.
        ///
        /// PlayerAudio.PlayRocketLauncherShotForAllClients handles its own networking.
        /// It is server-rate-limited to 5 calls per 0.5s; the default FireRate of 0.75s
        /// is well inside that, but a very low FireRate could have sounds dropped.
        /// </summary>
        private static void PlayFireEffects(PlayerInventory inventory)
        {
            inventory.PlayerInfo?.PlayerAudio?.PlayRocketLauncherShotForAllClients();

            // Mirrors the placement PlayRocketLaunchLocalOnly uses internally.
            Quaternion muzzleRotation = inventory.GetRocketLauncherRocketRotation();
            Quaternion backBlastRotation =
                Quaternion.AngleAxis(180f, inventory.transform.up) * muzzleRotation;

            PlayShotVfx(
                VfxType.RocketLauncherMuzzle,
                inventory.GetRocketLauncherBarrelFrontEndPosition(),
                muzzleRotation
            );

            PlayShotVfx(
                VfxType.RocketLauncherBackBlast,
                inventory.GetRocketLauncherBarrelBackEndPosition(),
                backBlastRotation
            );
        }

        /// <summary>
        /// Plays one pooled VFX for every player, from either a host or a pure client.
        /// </summary>
        private static void PlayShotVfx(VfxType vfxType, Vector3 position, Quaternion rotation)
        {
            if (NetworkServer.active)
            {
                // Host: RPC reaches the other clients only, so play it here too.
                VfxManager.PlayPooledVfxLocalOnly(vfxType, position, rotation);
                VfxManager.ServerPlayPooledVfxForAllClients(
                    vfxType,
                    position,
                    rotation,
                    connectionToSkip: NetworkServer.localConnection
                );
                return;
            }

            // Pure client: plays locally and forwards to everyone else.
            VfxManager.ClientPlayPooledVfxForAllClients(vfxType, position, rotation);
        }
    }
}
