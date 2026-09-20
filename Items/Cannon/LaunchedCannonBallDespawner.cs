using Mirror;
using UnityEngine;

namespace IssaPlugin.Items
{
    public class LaunchedCannonBallDespawner : MonoBehaviour
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