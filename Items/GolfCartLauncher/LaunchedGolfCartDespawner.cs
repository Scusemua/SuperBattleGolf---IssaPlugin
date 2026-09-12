using Mirror;
using UnityEngine;

namespace IssaPlugin.Items
{
    /// <summary>
    /// Server-only lifetime timer attached to a launched golf cart.
    ///
    /// Lives on the cart rather than on the shooter's NetworkBridge so the timer
    /// survives the shooter disconnecting mid-flight — a coroutine started on the
    /// shooter's player object would die with it and strand the cart until the
    /// hole ended.
    /// </summary>
    public class LaunchedGolfCartDespawner : MonoBehaviour
    {
        private float _remaining;

        /// <summary>Starts the countdown. Call on the server only.</summary>
        public void Initialize(float lifetimeSeconds) => _remaining = lifetimeSeconds;

        private void Update()
        {
            // Guard rather than assume: the component is only ever added server-side,
            // but a host is also a client and Update runs regardless.
            if (!NetworkServer.active)
                return;

            _remaining -= Time.deltaTime;
            if (_remaining > 0f)
                return;

            // Prevent a second Destroy if the object survives into the next frame.
            enabled = false;
            NetworkServer.Destroy(gameObject);
        }
    }
}
