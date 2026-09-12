using Mirror;
using UnityEngine;

namespace IssaPlugin.Items
{
    /// <summary>
    /// Damps a launched cart's leftover rotation once it touches down, so it settles
    /// and can be driven instead of tumbling away.
    ///
    /// A golf cart has no angular damping and no self-righting of its own — the only
    /// thing normally keeping it stable is its low centre of mass, which is enough for
    /// driving but not for absorbing a launch. A cart thrown through the air therefore
    /// keeps whatever spin the landing impact gives it, and even a flat four-wheel
    /// landing converts some of the drop into roll that never bleeds off.
    ///
    /// Runs on whichever peer owns the cart's physics. It is deliberately NOT a hard
    /// zeroing: the spin is eased out over SettleDuration so a cart that lands
    /// mid-tumble rights itself smoothly rather than snapping. Once settled, the
    /// component removes itself and the cart behaves exactly like any other.
    ///
    /// Tuned by SettleDamping / SettleDuration / SettleAngularThreshold; setting
    /// SettleDamping to 0 disables settling altogether.
    ///
    /// JoyrideSpinRetention is respected: a cart launched with deliberate spin keeps it
    /// while airborne, and only settles after it has actually landed.
    /// </summary>
    public class LaunchedGolfCartSettler : MonoBehaviour
    {
        private GolfCartInfo _cart;
        private Rigidbody _rigidbody;
        private float _groundedTime;

        public void Initialize(GolfCartInfo cart)
        {
            _cart = cart;
            _rigidbody = cart != null && cart.AsEntity != null ? cart.AsEntity.Rigidbody : null;

            // Nothing to damp without a body to damp.
            if (_rigidbody == null)
                enabled = false;
        }

        private void FixedUpdate()
        {
            if (_cart == null || _rigidbody == null)
            {
                Destroy(this);
                return;
            }

            if (_cart.Movement == null)
            {
                Destroy(this);
                return;
            }

            // Only whoever owns this cart's physics may damp it, or the two authorities
            // fight and the cart jitters.
            //
            // A Joyride rider occupies seat 0 from the moment of launch, so having a
            // driver is NOT a reason to stand down — that is exactly the case needing
            // settling. What matters is ownership: seating a REMOTE driver gives their
            // client authority over the cart (GolfCartInfo.ServerTryAssignPassengerToSeat),
            // so on that cart the server steps aside and the owning client runs its own
            // settler (attached in GolfCartLauncherNetworkBridge.HandleJoyrideLaunch).
            bool remoteDriverOwnsPhysics =
                _cart.TryGetDriver(out var driver)
                && driver != null
                && driver.connectionToClient != null
                && driver.connectionToClient != NetworkServer.localConnection;

            if (NetworkServer.active ? remoteDriverOwnsPhysics : !_cart.netIdentity.isOwned)
            {
                Destroy(this);
                return;
            }

            if (!_cart.Movement.IsAnyWheelGrounded())
            {
                // Still airborne: leave the launch spin alone.
                _groundedTime = 0f;
                return;
            }

            // Read lazily so an in-game config edit takes effect on the next landing.
            float damping = ModConfig.GolfCartLauncher.SettleDamping.Value;

            // 0 disables settling: leave the cart to tumble as raw physics dictates.
            // The spin ceiling is still restored, so the raised launch limit does not
            // outlive the launch and let later collisions over-spin the cart.
            if (damping <= 0f)
            {
                RestoreDefaultAngularLimit();
                Destroy(this);
                return;
            }

            float settleSeconds = ModConfig.GolfCartLauncher.SettleDuration.Value;
            float settledSpeed = ModConfig.GolfCartLauncher.SettleAngularThreshold.Value;

            _groundedTime += Time.fixedDeltaTime;

            Vector3 angular = _rigidbody.angularVelocity;
            if (angular.sqrMagnitude <= settledSpeed * settledSpeed)
            {
                // Settled — restore the stock spin ceiling and step out of the way.
                RestoreDefaultAngularLimit();
                Destroy(this);
                return;
            }

            // Exponential decay, framerate-independent.
            _rigidbody.angularVelocity = Vector3.Lerp(
                angular,
                Vector3.zero,
                1f - Mathf.Exp(-damping * Time.fixedDeltaTime)
            );

            if (_groundedTime >= settleSeconds)
            {
                _rigidbody.angularVelocity = Vector3.zero;
                RestoreDefaultAngularLimit();
                Destroy(this);
            }
        }

        /// <summary>
        /// Puts back Unity's default angular-velocity ceiling, which the launch raises
        /// so a configured tumble is not silently clamped. Leaving it raised would let
        /// later collisions spin the cart far faster than a normal one can.
        /// </summary>
        private void RestoreDefaultAngularLimit()
        {
            if (_rigidbody != null)
                _rigidbody.maxAngularVelocity = DefaultMaxAngularVelocity;
        }

        /// <summary>Unity's stock Rigidbody.maxAngularVelocity.</summary>
        private const float DefaultMaxAngularVelocity = 7f;
    }
}
