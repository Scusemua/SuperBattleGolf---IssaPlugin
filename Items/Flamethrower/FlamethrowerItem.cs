using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

namespace IssaPlugin.Items
{
    /// <summary>
    /// Flamethrower thrust logic — local-only physics, networked VFX + burn state
    /// via FlamethrowerNetworkBridge.
    ///
    /// Fuel model (mirrors Jetpack):
    ///   Each "use" (fuel canister) provides FuelPerUse seconds of continuous fire.
    ///   Releasing the button pauses fuel consumption; the next press resumes from
    ///   the same level. Only exhausting a full canister triggers DecrementAndRemove.
    ///
    /// Fire loop (mirrors AK-47):
    ///   Started by OnUse on button-down. Runs WaitForFixedUpdate each tick while
    ///   LMB is held. Hit detection is handled by the FlamethrowerHitDetector
    ///   MonoBehaviour attached to the flame particle prefab — this loop only
    ///   manages fuel and the VFX network notifications.
    /// </summary>
    public static class FlamethrowerItem
    {
        // Guard against a second coroutine starting if OnUse fires while already firing
        // (e.g. input buffer retry).  Static because only one local player fires at a time.
        private static bool _isFiring;

        // Persists across press/release so releasing the button does not refill the canister.
        // Sentinel -1 means "not yet initialised for this canister".
        private static float _fuelRemaining = -1f;

        /// <summary>
        /// Fraction of the current canister's fuel remaining [0, 1].
        /// 1.0 when not firing (full canister ready). Read by FlamethrowerOverlay.
        /// </summary>
        public static float FuelFraction { get; private set; } = 1f;

        /// <summary>True while the local player is actively firing.</summary>
        public static bool IsFiring => _isFiring;

        /// <summary>
        /// Resets fuel and clears the firing flag. Called from hole cleanup so the
        /// next hole always starts with a fresh canister and an unblocked fire loop.
        /// </summary>
        public static void ResetFiringState()
        {
            _fuelRemaining = -1f;
            _isFiring = false;
        }

        /// <summary>
        /// Starts the fire loop. Called by FlamethrowerItemDefinition.OnUse on button-down.
        /// </summary>
        public static IEnumerator FireLoop(PlayerInventory inventory)
        {
            // Guard: TryUseItem can be retried by the game's input buffer while the
            // Swing action buffer is still active. Exit immediately if the button is
            // not actually held — mirrors the same guard in JetpackItem.
            if (Mouse.current == null || !Mouse.current.leftButton.isPressed)
                yield break;

            if (_isFiring)
                yield break;

            _isFiring = true;

            var bridge = inventory.GetComponent<FlamethrowerNetworkBridge>();
            ItemHelper.SetCurrentItemUse(inventory, ItemUseType.Regular);
            bridge?.ClientNotifyFireStart();

            // Initialise fuel on first use of this canister.
            if (_fuelRemaining < 0f)
                _fuelRemaining = ModConfig.Flamethrower.FuelPerUse.Value;

            FuelFraction = Mathf.Clamp01(_fuelRemaining / ModConfig.Flamethrower.FuelPerUse.Value);

            try
            {
                do
                {
                    if (
                        inventory.GetEffectivelyEquippedItem(true)
                        != ItemRegistry.FlamethrowerItemType
                    )
                        break;

                    // Drain fuel by the fixed timestep for physics-accurate consumption.
                    _fuelRemaining -= Time.fixedDeltaTime;
                    FuelFraction = Mathf.Clamp01(
                        _fuelRemaining / ModConfig.Flamethrower.FuelPerUse.Value
                    );

                    if (_fuelRemaining <= 0f)
                    {
                        ItemHelper.DecrementAndRemove(inventory, inventory.EquippedItemIndex);

                        bool hasMore =
                            inventory.GetEffectivelyEquippedItem(true)
                            == ItemRegistry.FlamethrowerItemType;

                        if (!hasMore)
                            break;

                        // Reload the next canister.
                        _fuelRemaining = ModConfig.Flamethrower.FuelPerUse.Value;
                        FuelFraction = 1f;
                    }

                    yield return new WaitForFixedUpdate();
                } while (
                    Mouse.current != null
                    && Mouse.current.leftButton.isPressed
                    && _fuelRemaining > 0f
                );
            }
            finally
            {
                ItemHelper.SetCurrentItemUse(inventory, ItemUseType.None);
                bridge?.ClientNotifyFireStop();
                _isFiring = false;
                // Do NOT reset _fuelRemaining here — the canister level persists until
                // it is fully exhausted or the hole ends (see ResetFuel / ClientHoleCleanup).
            }
        }

    }
}
