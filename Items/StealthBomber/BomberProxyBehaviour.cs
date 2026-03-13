using IssaPlugin;
using Mirror;
using UnityEngine;

namespace IssaPlugin.Items
{
    /// Attached to the server-spawned BomberProxy GameObject.
    /// Moves the proxy along the bomber's calculated flight path each frame
    /// (server-only; NetworkTransform syncs the position to all clients).
    /// Counts rocket impacts via OnTriggerEnter  and invokes OnHitsExceeded when
    /// the configured hit threshold is reached.
    public class BomberProxyBehaviour : CustomHittable
    {
        public Vector3 SpawnPos;
        public Vector3 Direction;
        public float Speed;
        public float TotalDist;

        /// Set by the rocket patch to the world-space explosion position of the
        /// killing rocket, so the shot-down RPC can compute an impact direction.
        public Vector3 LastHitWorldPos;

        private float _startTime;
        private bool _shotDown;

        private void Awake()
        {
            Init();
        }

        private void Start()
        {
            Init();
        }

        private void Init()
        {
            _startTime = Time.time;

            HitCount = 0;
            HitsRequired = (int)Configuration.BomberHitsToDestroy.Value;
            OnHit += OnStealthBomberHit;
        }

        private void FixedUpdate()
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
        private void OnStealthBomberHit()
        {
            IssaPluginPlugin.Log.LogInfo($"[BomberProxy] OnStealthBomberHit called.");

            if (!NetworkServer.active || _shotDown)
                return;

            if (HitsRequired <= 0)
                return;

            if (HitCount >= HitsRequired)
                return;

            HitCount++;
            IssaPluginPlugin.Log.LogInfo($"[BomberProxy] Rocket impact {HitCount}/{HitsRequired}.");

            if (HitCount >= HitsRequired)
            {
                _shotDown = true;
                IssaPluginPlugin.Log.LogInfo("[BomberProxy] Shot down. Cancelling run.");
                OnHitsExceeded?.Invoke();
                Destroy(gameObject);
            }
        }
    }
}
