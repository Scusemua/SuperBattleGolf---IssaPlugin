using System.Collections.Generic;
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
        public void ClientRequestLaunch(Vector3 direction) =>
            NetworkClient.Send(new GolfCartLaunchRequestMessage { Direction = direction });

        // ================================================================
        //  Server
        // ================================================================

        /// <summary>
        /// Spawns and launches one golf cart. Registered in NetworkManagerPatches.
        /// </summary>
        public void ServerHandleLaunchRequest(Vector3 direction)
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

            ServerApplyLaunchImpulse(cart, direction);

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
        private static void ServerApplyLaunchImpulse(GolfCartInfo cart, Vector3 direction)
        {
            var rb = cart.AsEntity != null ? cart.AsEntity.Rigidbody : null;
            if (rb == null)
                return;

            // The cart is spawned driverless and airborne; a kinematic body would
            // ignore the launch entirely.
            rb.isKinematic = false;

            rb.linearVelocity = direction * ModConfig.GolfCartLauncher.LaunchSpeed.Value;

            // Spin is authored in degrees/second about the cart's own axes, which is
            // how a user thinks about "rotation on the golf carts"; Rigidbody wants
            // radians/second in world space.
            Vector3 localSpinDeg = new Vector3(
                ModConfig.GolfCartLauncher.SpinPitch.Value,
                ModConfig.GolfCartLauncher.SpinYaw.Value,
                ModConfig.GolfCartLauncher.SpinRoll.Value
            );

            rb.angularVelocity = cart.transform.TransformDirection(localSpinDeg * Mathf.Deg2Rad);

            // A tumbling cart easily exceeds Unity's default 7 rad/s cap, which would
            // silently clamp the configured spin.
            float requiredMax = rb.angularVelocity.magnitude;
            if (requiredMax > rb.maxAngularVelocity)
                rb.maxAngularVelocity = requiredMax;
        }

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

        public override void ClientHoleCleanup()
        {
            if (isLocalPlayer)
                GolfCartLauncherItem.ResetFiringState();
        }

        public override void OnStopServer()
        {
            ServerDestroyAllLaunchedCarts();
        }
    }
}
