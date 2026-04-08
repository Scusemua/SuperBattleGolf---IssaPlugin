using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

namespace IssaPlugin.Items
{
    /// <summary>
    /// Jetpack thrust logic — local-only physics, networked VFX via JetpackNetworkBridge.
    ///
    /// Fuel model: each "use" (canister) provides JetpackFuelPerUse seconds of thrust.
    /// Fuel persists across press/release cycles — releasing the activation button pauses
    /// consumption; the next press resumes from the same level. Only exhausting a full
    /// canister (fuel reaching zero) calls DecrementAndRemove and loads the next canister.
    ///
    /// Activation modes:
    ///   Normal (fromJump=false): triggered by LMB while the jetpack is the equipped item.
    ///     Thrust continues while LMB is held.
    ///   Jump (fromJump=true): triggered by Space when UseJumpToActivate is enabled.
    ///     Works even when the jetpack is not the equipped item; thrust continues while
    ///     Space is held; the specific inventory slot is tracked via slotOverride.
    ///
    /// Force is applied via AddForce inside a WaitForFixedUpdate coroutine so each call
    /// lands in the correct physics step — consistent with GravityGun and RocketTether.
    /// </summary>
    public static class JetpackItem
    {
        // Prevents a second coroutine starting if OnUse fires again while already flying
        // (e.g. if the game re-calls TryUseItem while held). Static because only one
        // local player can fly at a time.
        private static bool _isFlying;

        // Config values received from the server. -1 means not yet synced; fall back
        // to local config (which is correct on the host itself).
        internal static float ServerFuelPerUse = -1f;
        internal static float ServerThrustForce = -1f;

        // Persists across press/release cycles so releasing the button does not refill the canister.
        // Sentinel value -1 means "not yet initialised for this canister".
        private static float _fuelRemaining = -1f;

        /// <summary>
        /// True while the local player is actively thrusting. Read by JetpackOverlay.
        /// </summary>
        public static bool IsFlying => _isFlying;

        /// <summary>
        /// Fraction of the current canister's fuel remaining [0, 1].
        /// 1.0 when not flying (full canister ready). Updated each physics step during flight.
        /// Read by JetpackOverlay to render the fuel gauge.
        /// </summary>
        public static float FuelFraction { get; private set; } = 1f;

        /// <summary>
        /// Resets the current canister to full. Call from hole cleanup so that the next
        /// hole always starts with a fresh canister.
        /// </summary>
        public static void ResetFuel() => _fuelRemaining = -1f;

        /// <summary>
        /// Returns the index of the first inventory slot containing the jetpack, or -1 if
        /// none. Used to locate the jetpack when it may not be the equipped item.
        /// </summary>
        public static int FindJetpackSlot(PlayerInventory inventory)
        {
            for (int i = 0; i < inventory.slots.Count; i++)
            {
                if (inventory.slots[i].itemType == ItemRegistry.JetpackItemType)
                    return i;
            }
            return -1;
        }

        /// <summary>
        /// Starts the thrust loop. Called by JetpackItemDefinition.OnUse (normal/LMB mode)
        /// and by JetpackNetworkBridge.Update (jump mode).
        /// </summary>
        /// <param name="inventory">The local player's inventory.</param>
        /// <param name="fromJump">
        /// True when activated by the Jump (Space) key rather than LMB. In this mode the
        /// loop continues while Space is held instead of LMB, and slotOverride identifies
        /// which inventory slot holds the jetpack (it need not be equipped).
        /// </param>
        /// <param name="slotOverride">
        /// Inventory slot index of the jetpack. Ignored when fromJump is false (the
        /// equipped slot is used instead). Must be a valid slot index when fromJump is true.
        /// </param>
        public static IEnumerator FireLoop(
            PlayerInventory inventory,
            bool fromJump = false,
            int slotOverride = -1
        )
        {
            if (!fromJump)
            {
                // Normal mode: also trigger a jump for the initial liftoff.
                inventory.PlayerInfo.Movement.TryTriggerJump();
            }

            if (_isFlying)
                yield break;

            if (!fromJump)
            {
                // Guard: TryUseItem can be retried by the game's input buffer system at
                // moments other than the actual button-press (e.g. when the item is selected
                // while the Swing action buffer is still active). Without this check the
                // do-while executes one iteration before the condition is evaluated, which
                // the player perceives as thrust firing without pressing the mouse button.
                if (Mouse.current == null || !Mouse.current.leftButton.isPressed)
                    yield break;
            }
            else
            {
                // Jump mode: validate the slot before starting.
                if (slotOverride < 0)
                    yield break;
            }

            _isFlying = true;

            var bridge = inventory.GetComponent<JetpackNetworkBridge>();

            // Resolve the Rigidbody before notifying the server. If it is unavailable
            // we bail out here — no ThrustStart has been sent, so no ThrustStop is needed.
            Rigidbody rb = GameManager.LocalPlayerMovement?.GetComponent<Rigidbody>();
            if (rb == null)
            {
                _isFlying = false;
                yield break;
            }

            bridge?.ClientNotifyThrustStart();
            // In jump mode the jetpack is not the equipped item, so we must not set
            // CurrentItemUse — doing so would make IsUsingItemAtAll true and block the
            // player from switching items while thrusting.
            if (!fromJump)
                ItemHelper.SetCurrentItemUse(inventory, ItemUseType.Regular);

            // Prefer the host-synced fuel value so clients always use the server's config.
            float fuelPerUse =
                ServerFuelPerUse >= 0f ? ServerFuelPerUse : ModConfig.Jetpack.FuelPerUse.Value;

            // Initialise fuel on first use of this canister; subsequent presses resume
            // from the level at which the player last released the activation button.
            if (_fuelRemaining < 0f)
                _fuelRemaining = fuelPerUse;

            FuelFraction = Mathf.Clamp01(_fuelRemaining / fuelPerUse);

            try
            {
                do
                {
                    // Exit if the jetpack is no longer available in the relevant slot.
                    if (fromJump)
                    {
                        if (
                            slotOverride >= inventory.slots.Count
                            || inventory.slots[slotOverride].itemType
                                != ItemRegistry.JetpackItemType
                        )
                            break;
                    }
                    else
                    {
                        // Exit immediately if the player switched to another item mid-canister
                        // (e.g. pressed 1/2/3). Without this check thrust would ghost-fire
                        // until the canister timer naturally expired.
                        if (
                            inventory.GetEffectivelyEquippedItem(true)
                            != ItemRegistry.JetpackItemType
                        )
                            break;
                    }

                    // Apply upward thrust this physics step. ForceMode.Acceleration applies
                    // the configured value as m/s² regardless of the player's mass.
                    float thrustForce =
                        ServerThrustForce >= 0f
                            ? ServerThrustForce
                            : ModConfig.Jetpack.ThrustForce.Value;
                    rb.AddForce(Vector3.up * thrustForce, ForceMode.Acceleration);

                    // Drain fuel by the fixed timestep so consumption is physics-accurate
                    // and does not vary with render framerate.
                    _fuelRemaining -= Time.fixedDeltaTime;
                    FuelFraction = Mathf.Clamp01(_fuelRemaining / fuelPerUse);

                    if (_fuelRemaining <= 0f)
                    {
                        int slotToDrain = fromJump ? slotOverride : inventory.EquippedItemIndex;
                        ItemHelper.DecrementAndRemove(inventory, slotToDrain);

                        // Stop if all canisters are now exhausted.
                        bool hasMore = fromJump
                            ? slotOverride < inventory.slots.Count
                                && inventory.slots[slotOverride].itemType
                                    == ItemRegistry.JetpackItemType
                            : inventory.GetEffectivelyEquippedItem(true)
                                == ItemRegistry.JetpackItemType;

                        if (!hasMore)
                            break;

                        // Reload the next canister.
                        _fuelRemaining = fuelPerUse;
                        FuelFraction = 1f;
                    }

                    yield return new WaitForFixedUpdate();
                } while (
                    (
                        fromJump
                            ? Keyboard.current != null && Keyboard.current.spaceKey.isPressed
                            : Mouse.current != null && Mouse.current.leftButton.isPressed
                    ) && _fuelRemaining > 0
                );
            }
            finally
            {
                // finally block ensures these always run even if the hosting MonoBehaviour
                // is destroyed mid-flight (e.g. player disconnects), which would otherwise
                // leave _isFlying permanently true and orphan the server-side thrust state.
                if (!fromJump)
                    ItemHelper.SetCurrentItemUse(inventory, ItemUseType.None);
                bridge?.ClientNotifyThrustStop();
                _isFlying = false;
                // Do NOT reset _fuelRemaining here — the canister level persists until
                // it is fully exhausted or the hole ends (see ResetFuel / ClientHoleCleanup).
            }
        }
    }
}
