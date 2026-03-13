using Mirror;
using UnityEngine;

namespace IssaPlugin.Items
{
    /// <summary>
    /// Attached to the server-spawned BomberProxy GameObject.
    /// Moves the proxy along the bomber's calculated flight path each frame
    /// (server-only; NetworkTransform syncs the position to all clients).
    /// Counts rocket impacts via OnTriggerEnter and invokes OnShotDown when
    /// the configured hit threshold is reached.
    /// </summary>
    public class BomberProxyBehaviour : MonoBehaviour
    {
        public Vector3 SpawnPos;
        public Vector3 Direction;
        public float Speed;
        public float TotalDist;

        /// <summary>Invoked on the server when the hit threshold is reached.</summary>
        public System.Action OnShotDown;

        private float _startTime;
        private int _hitCount;
        private bool _shotDown;

        private void Start()
        {
            _startTime = Time.time;
        }

        private void Update()
        {
            // Movement is authoritative on the server only.
            // Clients receive position updates via NetworkTransform.
            if (!NetworkServer.active)
                return;

            float dist = (Time.time - _startTime) * Speed;
            transform.position = SpawnPos + Direction * dist;

            if (dist >= TotalDist)
                Destroy(gameObject);
        }

        // The game's Rocket uses a trigger collider.
        // This proxy has a kinematic Rigidbody + isTrigger SphereCollider so
        // OnTriggerEnter fires here when a rocket overlaps it.
        private void OnTriggerEnter(Collider other)
        {
            if (!NetworkServer.active || _shotDown)
                return;

            if (other.GetComponentInParent<Rocket>() == null)
                return;

            _hitCount++;
            int required = Configuration.BomberHitsToDestroy.Value;
            IssaPluginPlugin.Log.LogInfo($"[BomberProxy] Rocket impact {_hitCount}/{required}.");

            if (_hitCount >= required)
            {
                _shotDown = true;
                IssaPluginPlugin.Log.LogInfo("[BomberProxy] Shot down — cancelling run.");
                OnShotDown?.Invoke();
                Destroy(gameObject);
            }
        }

        /// <summary>
        /// Triggers the full shot-down sequence externally (e.g. from the orbital laser patch).
        /// Safe to call even if the hit threshold has not been reached via rockets.
        /// </summary>
        public void TriggerShotDown()
        {
            if (!NetworkServer.active || _shotDown)
                return;

            _shotDown = true;
            IssaPluginPlugin.Log.LogInfo("[BomberProxy] Shot down by orbital laser.");
            OnShotDown?.Invoke();
            Destroy(gameObject);
        }
    }
}
