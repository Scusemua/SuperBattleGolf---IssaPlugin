using System.Collections.Generic;
using HarmonyLib;
using IssaPlugin.Network;
using Mirror;
using UnityEngine;

namespace IssaPlugin.Items
{
    /// <summary>
    /// Attached to every player object via NetworkBridgePatches.
    ///
    /// ── Launch flow ──────────────────────────────────────────────────────────
    ///   1. The shooter's client resolves the aim direction locally
    ///      (GolfCartLauncherItem.Fire) and calls ClientRequestLaunch(dir).
    ///   2. Client sends GolfCartLaunchRequestMessage — direction only.
    ///   3. Server validates the shooter, instantiates the base game's own golf
    ///      cart prefab (GameManager.GolfCartSettings.Prefab), positions it in
    ///      front of the shooter, spawns it, then applies launch velocity and spin.
    ///   4. Mirror replicates the cart to every client for free — it is a normal
    ///      networked game entity, so no custom per-cart sync is needed.
    ///
    /// ── Damage ───────────────────────────────────────────────────────────────
    /// No custom hit detection: a victim's own PlayerMovement.OnCollisionEnter
    /// already handles golf-cart run-overs (KnockoutType.GolfCart) using
    /// GolfCartSettings' speed/knockback curves. Setting the cart's
    /// NetworkresponsiblePlayer to the shooter is all that is needed for the
    /// knockout to be attributed correctly.
    ///
    /// The cart is deliberately spawned WITHOUT a driver-seat reservation:
    /// ServerReserveDriverSeatPreNetworkSpawn would both consume an item-use hash
    /// the launcher never registered and schedule the base game's
    /// "destroy if nobody boards" timer.
    /// </summary>
    public class GolfCartLauncherNetworkBridge : NetworkBridgeBase
    {
        // Every cart launched by any player this hole, tracked so the server can
        // despawn them at the hole transition. Static because cleanup is global and
        // a launched cart outlives the interaction with its shooter's bridge.
        private static readonly List<GameObject> _serverLaunchedCarts = new();

        // ================================================================
        //  Client → Server
        // ================================================================

        /// <summary>
        /// Asks the server to launch a cart along <paramref name="direction"/>.
        /// Called by GolfCartLauncherItem.Fire on the shooter's client.
        /// </summary>
        public void ClientRequestLaunch(Vector3 direction, bool joyride, int equippedSlotIndex) =>
            NetworkClient.Send(
                new GolfCartLaunchRequestMessage
                {
                    Direction = direction,
                    Joyride = joyride,
                    EquippedSlotIndex = equippedSlotIndex,
                }
            );

        // ================================================================
        //  Server
        // ================================================================

        /// <summary>
        /// Spawns and launches one golf cart. Registered in NetworkManagerPatches.
        /// </summary>
        public void ServerHandleLaunchRequest(
            Vector3 direction,
            bool joyride,
            int equippedSlotIndex
        )
        {
            if (!isServer)
                return;

            // Reject a malformed or hostile direction rather than launching a cart
            // with NaN velocity, which would corrupt the physics scene for everyone.
            if (!IsFinite(direction) || direction.sqrMagnitude < 0.0001f)
                return;

            direction = direction.normalized;

            var shooter = GetComponent<PlayerInfo>();
            if (shooter == null)
                return;

            // Validate the sender actually holds the launcher, so a modified client
            // cannot spawn unlimited carts by sending this message directly.
            //
            // Checked against the authoritative `slots` SyncList at the index the client
            // sent, NOT against the live equipped item (the pattern PositionSwap and
            // ShapeShifter use). A liveness check races the client's own consumption:
            // on a host the decrement applies immediately, while this message only
            // arrives when Mirror drains the local connection queue, so the item can
            // already be gone by the time the server looks — rejecting valid shots
            // non-deterministically.
            var inventory = GetComponent<PlayerInventory>();
            if (
                inventory == null
                || ItemRegistry.GetItemTypeAtSlot(inventory, equippedSlotIndex)
                    != ItemRegistry.GolfCartLauncherItemType
            )
            {
                IssaPluginPlugin.Log.LogWarning(
                    "[GolfCartLauncher] Launch request from a player without the launcher equipped."
                );
                return;
            }

            // Joyride spends the whole item, so it is only legitimate at full uses.
            // The client already gates this; re-check so a modified client cannot ride
            // a cart for a single use.
            if (joyride)
            {
                // Uses are read from the same authoritative slot, for the same reason.
                int remainingUses = ItemRegistry.GetRemainingUsesAtSlot(
                    inventory,
                    equippedSlotIndex
                );
                int maxUses = ItemRegistry.GetMaxUses(ItemRegistry.GolfCartLauncherItemType);

                if (maxUses <= 0 || remainingUses < maxUses)
                    joyride = false;
            }

            var prefab = GameManager.GolfCartSettings?.Prefab;
            if (prefab == null)
            {
                IssaPluginPlugin.Log.LogWarning(
                    "[GolfCartLauncher] GolfCartSettings.Prefab is null; cannot launch a cart."
                );
                return;
            }

            // Spawn ahead of and above the shooter so the cart clears their own
            // collider instead of spawning inside it and knocking them over.
            Vector3 spawnPos =
                shooter.transform.position
                + direction * ModConfig.GolfCartLauncher.SpawnForwardOffset.Value
                + Vector3.up * ModConfig.GolfCartLauncher.SpawnVerticalOffset.Value;

            // Face the cart along its flight path, kept upright relative to world up.
            Quaternion spawnRot = Quaternion.LookRotation(direction, Vector3.up);

            GolfCartInfo cart = Object.Instantiate(prefab, spawnPos, spawnRot);
            if (cart == null)
                return;

            // The server drives this cart's flight; without this the host's local
            // client would own the transform and overwrite the launch velocity.
            ServerAuthoritativeTransform.Apply(cart.gameObject, "GolfCartLauncher");
            if (cart.Movement != null)
                cart.Movement.syncDirection = SyncDirection.ServerToClient;

            NetworkServer.Spawn(cart.gameObject);

            // Attribute run-over knockouts to the shooter. Set after Spawn so the
            // SyncVar change is replicated. The base game's ServerSetResponsiblePlayer
            // is private and starts a timeout that would clear attribution mid-flight,
            // so the SyncVar property is set directly instead.
            cart.NetworkresponsiblePlayer = shooter;

            // Seat the shooter BEFORE the impulse: ServerTryAssignPassengerToSeat can
            // reassign network authority over the cart, and doing that after setting
            // velocity would hand a moving cart to a client mid-flight.
            bool ownedByRemoteRider = false;
            if (joyride)
                ownedByRemoteRider = ServerSeatShooter(cart, shooter);

            ServerApplyLaunchImpulse(cart, direction, joyride);

            // A remote rider now owns the cart's physics, so the velocity written above
            // would be overwritten by their client. Ask the owner to apply it instead.
            //
            // Same ownership split the Black Hole Grenade uses: the server drives carts
            // with nobody aboard, and the occupant's own client drives the one they are
            // sitting in (see BlackHoleGrenadeBehaviour, which skips occupied carts, and
            // BlackHoleGrenadeNetworkBridge, which applies force to seat.golfCart).
            if (ownedByRemoteRider)
            {
                float speed = ModConfig.GolfCartLauncher.JoyrideLaunchSpeed.Value;
                var rb = cart.AsEntity != null ? cart.AsEntity.Rigidbody : null;

                shooter.connectionToClient?.Send(
                    new GolfCartJoyrideLaunchMessage
                    {
                        CartNetId = cart.netId,
                        Velocity = direction * speed,
                        // Reuse the spin ServerApplyLaunchImpulse just computed, so the
                        // owner reproduces exactly what the server intended.
                        AngularVelocity = rb != null ? rb.angularVelocity : Vector3.zero,
                    }
                );
            }

            // Drop entries whose cart is already gone (despawned by lifetime, driven
            // out of bounds, or destroyed by the game) so a long hole with heavy
            // launcher use does not grow this list without bound.
            _serverLaunchedCarts.RemoveAll(c => c == null);
            _serverLaunchedCarts.Add(cart.gameObject);

            // The timer lives on the cart, not on this bridge, so it keeps running if
            // the shooter disconnects while their cart is still in the air.
            float lifetime = ModConfig.GolfCartLauncher.Lifetime.Value;
            if (lifetime > 0f)
                cart.gameObject.AddComponent<LaunchedGolfCartDespawner>().Initialize(lifetime);
        }

        /// <summary>
        /// Gives the freshly spawned cart its flight velocity and tumble.
        /// </summary>
        private static void ServerApplyLaunchImpulse(
            GolfCartInfo cart,
            Vector3 direction,
            bool joyride
        )
        {
            var rb = cart.AsEntity != null ? cart.AsEntity.Rigidbody : null;
            if (rb == null)
                return;

            // The cart is spawned driverless and airborne; a kinematic body would
            // ignore the launch entirely.
            rb.isKinematic = false;

            float speed = joyride
                ? ModConfig.GolfCartLauncher.JoyrideLaunchSpeed.Value
                : ModConfig.GolfCartLauncher.LaunchSpeed.Value;
            rb.linearVelocity = direction * speed;

            // Spin is authored in degrees/second about the cart's own axes, which is
            // how a user thinks about "rotation on the golf carts"; Rigidbody wants
            // radians/second in world space.
            Vector3 localSpinDeg = new Vector3(
                ModConfig.GolfCartLauncher.SpinPitch.Value,
                ModConfig.GolfCartLauncher.SpinYaw.Value,
                ModConfig.GolfCartLauncher.SpinRoll.Value
            );

            // A rider gets a configurable fraction of the standard tumble. The default
            // is 0 (flies level), because a full tumble with a passenger aboard is hard
            // to read, but it is deliberately exposed for anyone who wants the chaos.
            if (joyride)
                localSpinDeg *= Mathf.Clamp01(
                    ModConfig.GolfCartLauncher.JoyrideSpinRetention.Value
                );

            rb.angularVelocity = cart.transform.TransformDirection(localSpinDeg * Mathf.Deg2Rad);

            // A tumbling cart easily exceeds Unity's default 7 rad/s cap, which would
            // silently clamp the configured spin.
            float requiredMax = rb.angularVelocity.magnitude;
            if (requiredMax > rb.maxAngularVelocity)
                rb.maxAngularVelocity = requiredMax;
        }

        /// <summary>
        /// Puts the shooter in the launched cart's driver seat (Joyride mode).
        ///
        /// Uses the base game's own GolfCartInfo.ServerTryAssignPassengerToSeat — the
        /// same method a player entering a parked cart goes through — so seat state,
        /// network authority and the driver's input mode are all set up exactly as the
        /// game expects. It is private, hence the cached reflection.
        ///
        /// The driver-seat RESERVATION path (ServerReserveDriverSeatPreNetworkSpawn) is
        /// deliberately not used: it validates a base-game item-use hash that a custom
        /// item never registers, so GolfCartInfo.OnStartClient would destroy the cart,
        /// and it also schedules the "destroy if nobody boards" timer.
        ///
        /// fromReservation is false so the cart is NOT teleported back to the player —
        /// it stays where it spawned, in front of them, and they are placed into it.
        /// </summary>
        /// <returns>
        /// True when the seated rider is a REMOTE client that now owns the cart's
        /// physics, meaning the launch velocity has to be applied on their machine.
        /// </returns>
        private static bool ServerSeatShooter(GolfCartInfo cart, PlayerInfo shooter)
        {
            if (AssignPassengerToSeatMethod == null)
            {
                IssaPluginPlugin.Log.LogWarning(
                    "[GolfCartLauncher] GolfCartInfo.ServerTryAssignPassengerToSeat not found; "
                        + "launching without a rider."
                );
                return false;
            }

            try
            {
                object result = AssignPassengerToSeatMethod.Invoke(
                    cart,
                    new object[] { shooter, DriverSeatIndex, false }
                );

                if (result is bool seated && !seated)
                    return false;

                // Seating seat 0 gives a non-host driver client authority over the cart
                // (see GolfCartInfo.ServerTryAssignPassengerToSeat).
                return shooter.connectionToClient != NetworkServer.localConnection;
            }
            catch (System.Exception ex)
            {
                // Never let a reflection failure take down the launch: the cart is
                // already spawned, so the shot still happens, just without the rider.
                IssaPluginPlugin.Log.LogError(
                    $"[GolfCartLauncher] Failed to seat the shooter in the launched cart: {ex}"
                );
                return false;
            }
        }

        /// <summary>
        /// Applies the launch velocity to a cart this client owns. Registered as the
        /// GolfCartJoyrideLaunchMessage handler in NetworkManagerPatches.
        /// </summary>
        public static void HandleJoyrideLaunch(GolfCartJoyrideLaunchMessage msg)
        {
            if (!NetworkClient.spawned.TryGetValue(msg.CartNetId, out var identity))
                return;

            var cart = identity.GetComponent<GolfCartInfo>();
            var rb = cart != null && cart.AsEntity != null ? cart.AsEntity.Rigidbody : null;
            if (rb == null)
                return;

            rb.isKinematic = false;
            rb.linearVelocity = msg.Velocity;
            rb.angularVelocity = msg.AngularVelocity;

            // Unity clamps angular velocity to 7 rad/s by default, which would silently
            // eat a configured tumble.
            float requiredMax = msg.AngularVelocity.magnitude;
            if (requiredMax > rb.maxAngularVelocity)
                rb.maxAngularVelocity = requiredMax;
        }

        /// <summary>Driver is always seat 0 (see GolfCartInfo.ServerTryAssignPassengerToSeat).</summary>
        private const int DriverSeatIndex = 0;

        private static readonly System.Reflection.MethodInfo AssignPassengerToSeatMethod =
            AccessTools.Method(typeof(GolfCartInfo), "ServerTryAssignPassengerToSeat");

        private static bool IsFinite(Vector3 v) =>
            !float.IsNaN(v.x)
            && !float.IsNaN(v.y)
            && !float.IsNaN(v.z)
            && !float.IsInfinity(v.x)
            && !float.IsInfinity(v.y)
            && !float.IsInfinity(v.z);

        // ================================================================
        //  Cleanup
        // ================================================================

        /// <summary>
        /// Destroys every cart launched this hole. Runs on the first bridge to be
        /// cleaned up; the list is static and shared, so later calls are no-ops.
        /// </summary>
        private static void ServerDestroyAllLaunchedCarts()
        {
            foreach (var cart in _serverLaunchedCarts)
            {
                if (cart != null)
                    NetworkServer.Destroy(cart);
            }

            _serverLaunchedCarts.Clear();
        }

        public override void ServerHoleCleanup()
        {
            ServerDestroyAllLaunchedCarts();
        }

        /// <summary>
        /// Clears the local player's firing and Joyride state at a hole transition.
        ///
        /// GolfCartLauncherItem's state is static and describes the LOCAL player only —
        /// the fire loop and the Joyride toggle both run on the shooting client, and
        /// nothing about them is per-bridge. There is deliberately no isLocalPlayer
        /// guard here: the call is already idempotent, and guarding it would imply a
        /// per-player scope that the state does not have.
        ///
        /// Plugin.OnMatchStateChanged only invokes this on the local player's bridges,
        /// so it runs once per hole regardless.
        /// </summary>
        public override void ClientHoleCleanup()
        {
            GolfCartLauncherItem.ResetFiringState();
        }

        public override void OnStopServer()
        {
            ServerDestroyAllLaunchedCarts();
        }
    }
}
