using UnityEngine;

namespace IssaPlugin.Items
{
    /// <summary>
    /// Server-only kinematic movement driver for the Harrier Jet object.
    ///
    /// This component is added programmatically by HarrierNetworkBridge after
    /// NetworkServer.Spawn, so it only exists on the server instance. Clients
    /// receive position/rotation updates through the prefab's NetworkTransform.
    ///
    /// Three phases:
    ///   FlyIn  — fly from the spawn point toward HoverTarget at ApproachSpeed.
    ///   Hover  — hold altitude while slowly drifting toward the player centroid,
    ///            clamped to HoverRadius from the initial center.
    ///   FlyOut — accelerate away toward FlyOutTarget; sets IsComplete when done.
    /// </summary>
    public class HarrierBehaviour : MonoBehaviour
    {
        // ================================================================
        //  Configuration — set by HarrierNetworkBridge immediately after attach
        // ================================================================

        public Vector3 HoverTarget;
        public Vector3 FlyOutTarget;
        public float ApproachSpeed;
        public float DriftSpeed;
        public float HoverRadius;

        // ================================================================
        //  Public state read by HarrierNetworkBridge
        // ================================================================

        public bool HasArrived => _phase != Phase.FlyIn;
        public bool IsComplete { get; private set; }

        // ================================================================
        //  Internal state
        // ================================================================

        private enum Phase
        {
            FlyIn,
            Hover,
            FlyOut,
        }

        private Phase _phase = Phase.FlyIn;
        private Rigidbody _rb;

        private Vector3 _initialCenter;
        private Vector3 _currentDriftTarget;
        private float _driftRefreshTimer;

        private const float DriftRefreshInterval = 1.5f;
        private const float ArrivalThreshold = 4f;
        private const float FlyOutCompleteThreshold = 10f;
        private const float FlyOutSpeedMultiplier = 1.5f;

        // ================================================================
        //  Unity lifecycle
        // ================================================================

        private void Awake()
        {
            _rb = GetComponent<Rigidbody>();
            if (_rb == null)
                _rb = gameObject.AddComponent<Rigidbody>();
            _rb.isKinematic = true;
            _rb.useGravity = false;
        }

        private void Start()
        {
            _initialCenter = HoverTarget;
            _currentDriftTarget = HoverTarget;
        }

        private void FixedUpdate()
        {
            switch (_phase)
            {
                case Phase.FlyIn:
                    UpdateFlyIn();
                    break;
                case Phase.Hover:
                    UpdateHover();
                    break;
                case Phase.FlyOut:
                    UpdateFlyOut();
                    break;
            }
        }

        // ================================================================
        //  Phase updates
        // ================================================================

        private void UpdateFlyIn()
        {
            Vector3 toTarget = HoverTarget - transform.position;
            float dist = toTarget.magnitude;

            if (dist <= ArrivalThreshold)
            {
                _rb.MovePosition(HoverTarget);
                _phase = Phase.Hover;
                return;
            }

            Vector3 step = toTarget.normalized * (ApproachSpeed * Time.fixedDeltaTime);
            if (step.magnitude > dist)
                step = toTarget;

            _rb.MovePosition(transform.position + step);

            if (toTarget.sqrMagnitude > 0.01f)
                _rb.MoveRotation(Quaternion.LookRotation(toTarget.normalized));
        }

        private void UpdateHover()
        {
            // Periodically recompute where the player group is concentrated.
            _driftRefreshTimer -= Time.fixedDeltaTime;
            if (_driftRefreshTimer <= 0f)
            {
                _driftRefreshTimer = DriftRefreshInterval;
                RefreshDriftTarget();
            }

            Vector3 toDrift = _currentDriftTarget - transform.position;
            if (toDrift.magnitude > 0.1f)
            {
                Vector3 step = toDrift.normalized * (DriftSpeed * Time.fixedDeltaTime);
                if (step.magnitude > toDrift.magnitude)
                    step = toDrift;
                _rb.MovePosition(transform.position + step);
            }

            // Face the horizontal drift direction when moving.
            Vector3 horzDrift = new Vector3(toDrift.x, 0f, toDrift.z);
            if (horzDrift.magnitude > 0.5f)
                _rb.MoveRotation(Quaternion.LookRotation(horzDrift.normalized));
        }

        private void UpdateFlyOut()
        {
            Vector3 toExit = FlyOutTarget - transform.position;
            if (toExit.magnitude <= FlyOutCompleteThreshold)
            {
                IsComplete = true;
                return;
            }

            float speed = ApproachSpeed * FlyOutSpeedMultiplier;
            _rb.MovePosition(
                transform.position + toExit.normalized * (speed * Time.fixedDeltaTime)
            );
            _rb.MoveRotation(Quaternion.LookRotation(toExit.normalized));
        }

        // ================================================================
        //  Called by HarrierNetworkBridge when the attack phase ends
        // ================================================================

        public void BeginFlyOut()
        {
            _phase = Phase.FlyOut;
        }

        // ================================================================
        //  Helpers
        // ================================================================

        private void RefreshDriftTarget()
        {
            var players = FindObjectsByType<PlayerInventory>(FindObjectsSortMode.None);
            if (players.Length == 0)
            {
                _currentDriftTarget = _initialCenter;
                return;
            }

            Vector3 sum = Vector3.zero;
            foreach (var p in players)
                sum += p.transform.position;

            Vector3 centroid = sum / players.Length;
            centroid.y = _initialCenter.y; // Hold altitude fixed.

            // Clamp drift to HoverRadius so the jet doesn't wander too far.
            Vector3 offset = centroid - _initialCenter;
            if (offset.magnitude > HoverRadius)
                centroid = _initialCenter + offset.normalized * HoverRadius;

            _currentDriftTarget = centroid;
        }
    }
}
