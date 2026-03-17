using System.Reflection;
using UnityEngine;

namespace IssaPlugin.Items
{
    /// Drives the Javelin rocket through its three-phase lofted arc entirely
    /// on the server.  Attach this to the Rocket GameObject immediately after
    /// NetworkServer.Spawn so it starts updating on the first FixedUpdate.
    ///
    /// Phase 1 – Ascending:  rocket climbs steeply upward from the launch point.
    /// Phase 2 – Turning:    at apex altitude the rocket smoothly rotates to
    ///                        point at the locked target.
    /// Phase 3 – Diving:     rocket accelerates straight down toward the target;
    ///                        when it arrives (or gets close) ServerExplode is called.
    ///
    /// All physics are applied by directly setting the Rigidbody velocity each
    /// FixedUpdate so the standard Rocket distance-limit code never kicks in (the
    /// same trick used by PredatorMissileItem via RocketFixedBUpdatePatch).
    public class JavelinRocketBehaviour : MonoBehaviour
    {
        // ----------------------------------------------------------------
        //  Configuration (set by JavelinItem before this component starts)
        // ----------------------------------------------------------------

        /// World-space position the rocket will dive toward.
        public Vector3 TargetPosition;

        /// How high above the launch point the rocket climbs before turning.
        public float ApexHeightAboveLaunch = 60f;

        /// Speed during the ascent phase (units/sec).
        public float AscentSpeed = 35f;

        /// Speed during the dive phase (units/sec, continuously accelerated).
        public float DiveSpeed = 50f;

        /// Acceleration added to dive speed each second.
        public float DiveAcceleration = 20f;

        /// Horizontal distance from target at which we consider it a hit.
        public float ArrivalRadius = 3f;

        /// How quickly the rocket rotates to face the target during the
        /// turning phase (degrees per second).
        public float TurnRate = 180f;

        // ----------------------------------------------------------------
        //  Internal state
        // ----------------------------------------------------------------

        private enum Phase
        {
            Ascending,
            Turning,
            Diving,
        }

        private Phase _phase = Phase.Ascending;

        private Rocket _rocket;
        private Rigidbody _rb;
        private float _apexY; // world Y the rocket must reach before turning
        private float _currentDiveSpeed;
        private bool _exploded;

        // Cached reflection handle for Rocket.ServerExplode (private method).
        private static readonly MethodInfo ServerExplodeMethod = typeof(Rocket).GetMethod(
            "ServerExplode",
            BindingFlags.NonPublic | BindingFlags.Instance
        );

        // Same field zeroed by RocketFixedBUpdatePatch — we zero it ourselves
        // since this component is server-only and the patch is still active.
        private static readonly FieldInfo DistanceTravelledField = typeof(Rocket).GetField(
            "distanceTravelled",
            BindingFlags.NonPublic | BindingFlags.Instance
        );

        // ----------------------------------------------------------------
        //  Unity lifecycle
        // ----------------------------------------------------------------

        private void Start()
        {
            _rocket = GetComponent<Rocket>();
            _rb = GetComponent<Rigidbody>();

            if (_rb == null)
            {
                var entity = GetComponent<Entity>();
                if (entity != null && entity.HasRigidbody)
                    _rb = entity.Rigidbody;
            }

            _apexY = transform.position.y + ApexHeightAboveLaunch;
            _currentDiveSpeed = DiveSpeed;

            IssaPluginPlugin.Log.LogInfo(
                $"[Javelin] Rocket launched from {transform.position} toward {TargetPosition}. "
                    + $"Apex Y={_apexY:F1}"
            );
        }

        private void FixedUpdate()
        {
            if (_exploded || _rocket == null)
                return;

            // Zero distanceTravelled so Rocket's own range-limit never fires.
            DistanceTravelledField?.SetValue(_rocket, 0f);

            switch (_phase)
            {
                case Phase.Ascending:
                    UpdateAscending();
                    break;
                case Phase.Turning:
                    UpdateTurning();
                    break;
                case Phase.Diving:
                    UpdateDiving();
                    break;
            }
        }

        // ----------------------------------------------------------------
        //  Phase updates
        // ----------------------------------------------------------------

        private void UpdateAscending()
        {
            if (_rb == null)
                return;

            // Fly straight up.
            _rb.linearVelocity = Vector3.up * AscentSpeed;

            // Orient the rocket nose-up while ascending.
            transform.rotation = Quaternion.LookRotation(Vector3.up, Vector3.forward);

            if (transform.position.y >= _apexY)
            {
                _rb.linearVelocity = Vector3.zero;
                _phase = Phase.Turning;
                IssaPluginPlugin.Log.LogInfo("[Javelin] Apex reached — beginning turn.");
            }
        }

        private void UpdateTurning()
        {
            if (_rb == null)
                return;

            // Keep rocket stationary while it rotates to face the target.
            _rb.linearVelocity = Vector3.zero;

            Vector3 toTarget = (TargetPosition - transform.position).normalized;
            if (toTarget == Vector3.zero)
                toTarget = Vector3.down;

            Quaternion targetRot = Quaternion.LookRotation(toTarget, Vector3.up);
            transform.rotation = Quaternion.RotateTowards(
                transform.rotation,
                targetRot,
                TurnRate * Time.fixedDeltaTime
            );

            float angle = Quaternion.Angle(transform.rotation, targetRot);
            if (angle < 5f)
            {
                _phase = Phase.Diving;
                _currentDiveSpeed = DiveSpeed;
                IssaPluginPlugin.Log.LogInfo("[Javelin] Turn complete — beginning dive.");
            }
        }

        private void UpdateDiving()
        {
            if (_rb == null)
                return;

            // Accelerate toward target.
            _currentDiveSpeed += DiveAcceleration * Time.fixedDeltaTime;

            Vector3 toTarget = (TargetPosition - transform.position).normalized;
            if (toTarget == Vector3.zero)
                toTarget = Vector3.down;

            _rb.linearVelocity = toTarget * _currentDiveSpeed;

            // Keep nose pointed at the target.
            transform.rotation = Quaternion.LookRotation(toTarget, Vector3.up);

            // Check arrival — use horizontal+vertical distance to handle hills.
            float dist = Vector3.Distance(
                new Vector3(transform.position.x, 0, transform.position.z),
                new Vector3(TargetPosition.x, 0, TargetPosition.z)
            );

            bool pastTarget =
                transform.position.y <= TargetPosition.y + 2f && dist < ArrivalRadius * 2f;
            bool veryClose = Vector3.Distance(transform.position, TargetPosition) < ArrivalRadius;

            if (veryClose || pastTarget)
            {
                Detonate();
            }
        }

        // ----------------------------------------------------------------
        //  Detonation
        // ----------------------------------------------------------------

        public void Detonate()
        {
            if (_exploded)
                return;
            _exploded = true;

            if (_rocket == null)
                return;

            IssaPluginPlugin.Log.LogInfo(
                $"[Javelin] Detonating at {transform.position} (target was {TargetPosition})."
            );

            ServerExplodeMethod?.Invoke(_rocket, new object[] { transform.position });
        }
    }
}
