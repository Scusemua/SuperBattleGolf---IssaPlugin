using System;
using System.Reflection;
using HarmonyLib;
using Mirror;
using UnityEngine;

namespace IssaPlugin.Items
{
    /// <summary>
    /// Attached to every player object via NetworkBridgePatches.
    ///
    /// When the owning client uses the Sports Car item, a SportsCarActivateMessage is sent
    /// to the server, which validates the request, consumes the item, spawns a golf cart
    /// prefab with boosted movement settings, and seats the player in it as driver.
    /// </summary>
    public class SportsCarNetworkBridge : NetworkBridgeBase
    {
        private static readonly MethodInfo ServerEnterMethod = typeof(GolfCartInfo).GetMethod(
            "ServerEnter",
            BindingFlags.Instance | BindingFlags.NonPublic,
            null, new[] { typeof(PlayerInfo) }, null);

        private static readonly FieldInfo SettingsField =
            AccessTools.Field(typeof(GolfCartMovement), "settings");

        // ================================================================
        //  Client → Server
        // ================================================================

        public void ServerSpawnSportsCar()
        {
            if (!NetworkServer.active) return;

            var inventory = GetComponent<PlayerInventory>();
            if (inventory == null) return;

            var equipped = inventory.GetEffectivelyEquippedItem(true);
            if (equipped != ItemRegistry.SportsCarItemType)
            {
                IssaPluginPlugin.Log.LogWarning("[SportsCar] Player does not have Sports Car item equipped.");
                return;
            }

            var player = GetComponent<PlayerInfo>();
            if (player == null) return;

            var prefab = GameManager.GolfCartSettings?.Prefab;
            if (prefab == null)
            {
                IssaPluginPlugin.Log.LogError("[SportsCar] GolfCartSettings.Prefab is null.");
                return;
            }

            ItemHelper.ConsumeEquippedItem(inventory);

            var cart = UnityEngine.Object.Instantiate(
                prefab,
                player.transform.position,
                Quaternion.Euler(0f, player.transform.eulerAngles.y, 0f));

            if (cart == null)
            {
                IssaPluginPlugin.Log.LogError($"[SportsCar] Cart failed to instantiate for {player.PlayerId.PlayerName}.");
                return;
            }

            // Override movement settings with sports car values.
            // Object.Instantiate on a ScriptableObject copies all Unity-serialized fields,
            // producing an independent asset that can be tuned without affecting the original.
            var movement = cart.GetComponent<GolfCartMovement>();
            if (movement != null && SettingsField != null)
            {
                var original = (GolfCartMovementSettings)SettingsField.GetValue(movement);
                if (original != null)
                {
                    var boosted = UnityEngine.Object.Instantiate(original);

                    // Safety: if the copy has no forward speed the serialized backing fields
                    // weren't copied (shouldn't happen, but be defensive).
                    if (boosted.MaxForwardSpeed > 0f)
                    {
                        ApplySportsCarSettings(boosted, original);
                        SettingsField.SetValue(movement, boosted);
                    }
                    else
                    {
                        IssaPluginPlugin.Log.LogWarning(
                            "[SportsCar] Movement settings copy has zero MaxForwardSpeed — " +
                            "falling back to default golf cart physics.");
                        UnityEngine.Object.Destroy(boosted);
                    }
                }
            }

            cart.ServerReserveDriverSeatPreNetworkSpawn(player);
            NetworkServer.Spawn(cart.gameObject);
            cart.ServerReserveDriverSeatPostNetworkSpawn();

            try
            {
                ServerEnterMethod?.Invoke(cart, new object[] { player });
                IssaPluginPlugin.Log.LogInfo($"[SportsCar] Spawned sports car for {player.PlayerId.PlayerName}.");
            }
            catch (Exception ex)
            {
                IssaPluginPlugin.Log.LogError($"[SportsCar] ServerEnter failed for {player.PlayerId.PlayerName}: {ex.Message}");
            }
        }

        // ================================================================
        //  Movement settings override
        // ================================================================

        private static void ApplySportsCarSettings(GolfCartMovementSettings boosted, GolfCartMovementSettings original)
        {
            var t = typeof(GolfCartMovementSettings);
            float speedMult  = Configuration.SportsCarSpeedMultiplier.Value;
            float torqueMult = Configuration.SportsCarTorqueMultiplier.Value;

            void Set(string prop, float value) =>
                AccessTools.Property(t, prop)?.SetValue(boosted, value);

            // Speed caps — the global MatchSetupRules.CartSpeed factor stacks on top.
            Set("MaxForwardSpeed",  original.MaxForwardSpeed  * speedMult);
            Set("MaxBackwardSpeed", original.MaxBackwardSpeed * speedMult);

            // Acceleration torque.
            Set("MaxForwardAccelerationTorque",  original.MaxForwardAccelerationTorque  * torqueMult);
            Set("MaxBackwardAccelerationTorque", original.MaxBackwardAccelerationTorque * torqueMult);

            // Attenuation thresholds scale with speed so torque ramps down at the right point.
            Set("ForwardAccelerationAttenuationSpeedThreshold",
                original.ForwardAccelerationAttenuationSpeedThreshold  * speedMult);
            Set("BackwardAccelerationAttenuationSpeedThreshold",
                original.BackwardAccelerationAttenuationSpeedThreshold * speedMult);

            // Max-speed-exceeded damping thresholds also need to scale with the new top speed.
            Set("MaxSpeedExceededMaxWheelDampingForwardSpeedThreshold",
                original.MaxSpeedExceededMaxWheelDampingForwardSpeedThreshold  * speedMult);
            Set("MaxSpeedExceededMaxWheelDampingBackwardSpeedThreshold",
                original.MaxSpeedExceededMaxWheelDampingBackwardSpeedThreshold * speedMult);

            // Driverless auto-brake thresholds scale with speed so the cart doesn't
            // instantly lock up when unoccupied at higher velocity.
            Set("DriverlessMinBrakeSpeed", original.DriverlessMinBrakeSpeed * speedMult);
            Set("DriverlessMaxBrakeSpeed", original.DriverlessMaxBrakeSpeed * speedMult);
        }

        // ================================================================
        //  IHoleCleanable — nothing to clean up (cart is a scene object)
        // ================================================================

        public override void ServerHoleCleanup() { }
        public override void ClientHoleCleanup() { }
    }
}
