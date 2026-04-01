using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

namespace IssaPlugin.Items
{
    /// <summary>
    /// Jetpack thrust logic — local-only physics, networked VFX via JetpackNetworkBridge.
    ///
    /// Fuel model: each "use" (canister) provides JetpackFuelPerUse seconds of thrust.
    /// Fuel persists across press/release cycles — releasing LMB pauses consumption;
    /// the next press resumes from the same level. Only exhausting a full canister
    /// (fuel reaching zero) calls DecrementAndRemove and loads the next canister.
    /// Releasing LMB or switching items also exits the loop cleanly.
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

        // Persists across press/release cycles so releasing LMB does not refill the canister.
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

        public static IEnumerator FireLoop(PlayerInventory inventory)
        {
            inventory.PlayerInfo.Movement.TryTriggerJump();

            if (_isFlying)
                yield break;

            // Guard: TryUseItem can be retried by the game's input buffer system at moments
            // other than the actual button-press (e.g. when the item is selected while the
            // Swing action buffer is still active). Without this check the do-while executes
            // one iteration — applying a single frame of upward force — before the condition
            // is evaluated, which the player perceives as thrust firing without pressing the
            // mouse button.
            if (Mouse.current == null || !Mouse.current.leftButton.isPressed)
                yield break;

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
            ItemHelper.SetCurrentItemUse(inventory, ItemUseType.Regular);

            // Initialise fuel on first use of this canister; subsequent presses resume
            // from the level at which the player last released LMB.
            if (_fuelRemaining < 0f)
                _fuelRemaining = Configuration.JetpackFuelPerUse.Value;

            FuelFraction = Mathf.Clamp01(_fuelRemaining / Configuration.JetpackFuelPerUse.Value);

            try
            {
                do
                {
                    // Exit immediately if the player switched to another item mid-canister
                    // (e.g. pressed 1/2/3). Without this check thrust would ghost-fire until
                    // the current canister timer naturally expired.
                    if (inventory.GetEffectivelyEquippedItem(true) != ItemRegistry.JetpackItemType)
                        break;

                    // Apply upward thrust this physics step. ForceMode.Acceleration applies
                    // the configured value as m/s² regardless of the player's mass.
                    rb.AddForce(
                        Vector3.up * Configuration.JetpackThrustForce.Value,
                        ForceMode.Acceleration
                    );

                    // Drain fuel by the fixed timestep so consumption is physics-accurate
                    // and does not vary with render framerate.
                    _fuelRemaining -= Time.fixedDeltaTime;
                    FuelFraction = Mathf.Clamp01(
                        _fuelRemaining / Configuration.JetpackFuelPerUse.Value
                    );

                    if (_fuelRemaining <= 0f)
                    {
                        int slot = inventory.EquippedItemIndex;
                        ItemHelper.DecrementAndRemove(inventory, slot);

                        // Stop if all canisters are now exhausted.
                        if (
                            inventory.GetEffectivelyEquippedItem(true)
                            != ItemRegistry.JetpackItemType
                        )
                            break;

                        // Reload the next canister — re-read config so in-session changes
                        // take effect between canisters.
                        _fuelRemaining = Configuration.JetpackFuelPerUse.Value;
                        FuelFraction = 1f;
                    }

                    yield return new WaitForFixedUpdate();
                } while (
                    Mouse.current != null
                    && Mouse.current.leftButton.isPressed
                    && _fuelRemaining > 0
                );
            }
            finally
            {
                // finally block ensures these always run even if the hosting MonoBehaviour
                // is destroyed mid-flight (e.g. player disconnects), which would otherwise
                // leave _isFlying permanently true and orphan the server-side thrust state.
                ItemHelper.SetCurrentItemUse(inventory, ItemUseType.None);
                bridge?.ClientNotifyThrustStop();
                _isFlying = false;
                // Do NOT reset _fuelRemaining here — the canister level persists until
                // it is fully exhausted or the hole ends (see ResetFuel / ClientHoleCleanup).
            }
        }
    }
}
