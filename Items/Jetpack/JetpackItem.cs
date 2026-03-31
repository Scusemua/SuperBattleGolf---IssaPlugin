using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

namespace IssaPlugin.Items
{
    /// <summary>
    /// Jetpack thrust logic — local-only physics, networked VFX via JetpackNetworkBridge.
    ///
    /// Fuel model: each "use" (canister) provides JetpackFuelPerUse seconds of thrust.
    /// The loop drains fuel by Time.fixedDeltaTime each physics step. On canister
    /// exhaustion DecrementAndRemove is called. When all canisters are gone the loop exits.
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

        public static IEnumerator FireLoop(PlayerInventory inventory)
        {
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

            float fuelRemaining = Configuration.JetpackFuelPerUse.Value;

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
                    fuelRemaining -= Time.fixedDeltaTime;

                    if (fuelRemaining <= 0f)
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
                        fuelRemaining = Configuration.JetpackFuelPerUse.Value;
                    }

                    yield return new WaitForFixedUpdate();
                } while (Mouse.current != null && Mouse.current.leftButton.isPressed);
            }
            finally
            {
                // finally block ensures these always run even if the hosting MonoBehaviour
                // is destroyed mid-flight (e.g. player disconnects), which would otherwise
                // leave _isFlying permanently true and orphan the server-side thrust state.
                ItemHelper.SetCurrentItemUse(inventory, ItemUseType.None);
                bridge?.ClientNotifyThrustStop();
                _isFlying = false;
            }
        }
    }
}
