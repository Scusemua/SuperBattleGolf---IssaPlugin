using System.Reflection;
using Mirror;
using UnityEngine;

namespace IssaPlugin.Items
{
    /// <summary>
    /// Drives the Javelin rocket through its three-phase lofted arc entirely
    /// on the server.  Attach this to the Rocket GameObject immediately after
    /// NetworkServer.Spawn so it starts updating on the first FixedUpdate.
    ///
    /// Phase 1 – Ascending:  rocket climbs steeply upward from the launch point.
    /// Phase 2 – Turning:    at apex altitude the rocket smoothly rotates to
    ///                        point at the locked target.
    /// Phase 3 – Diving:     rocket accelerates toward the target and detonates
    ///                        on arrival.
    ///
    /// Physics are driven by setting Rigidbody.linearVelocity directly each
    /// FixedUpdate.  Rocket.OnFixedBUpdate is suppressed for this rocket via
    /// RocketFixedBUpdatePatch so the game never overwrites our velocity.
    ///
    /// Rotation is cosmetic only — the nose tracks the current velocity direction
    /// via a safe slerp that is skipped entirely when velocity is zero, which
    /// is the only reliable way to avoid "Look rotation viewing vector is zero".
    /// </summary>
    public class JavelinRocketBehaviour : MonoBehaviour
    {
        // ----------------------------------------------------------------
        //  Configuration  (set by JavelinNetworkBridge before Start fires)
        // ----------------------------------------------------------------

        public Vector3 TargetPosition;
        public float ApexHeightAboveLaunch = 60f;
        public float AscentSpeed = 35f;
        public float DiveSpeed = 55f;
        public float DiveAcceleration = 25f;
        public float ArrivalRadius = 3f;
        public float TurnRate = 180f; // degrees/sec during Turning phase

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
        private float _apexY;
        private float _currentDiveSpeed;
        private bool _exploded;

        private static readonly MethodInfo ServerExplodeMethod = typeof(Rocket).GetMethod(
            "ServerExplode",
            BindingFlags.NonPublic | BindingFlags.Instance
        );

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

            if (_rocket != null)
                JavelinItem.ActiveJavelinRockets.Add(_rocket);

            _apexY = transform.position.y + ApexHeightAboveLaunch;
            _currentDiveSpeed = DiveSpeed;

            IssaPluginPlugin.Log.LogInfo(
                $"[Javelin] Launched from {transform.position:F1} "
                    + $"toward {TargetPosition:F1}. ApexY={_apexY:F1}"
            );
        }

        private void FixedUpdate()
        {
            if (_exploded || _rocket == null)
                return;

            // Prevent Rocket's own range-limit from firing.
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
        //  Phases
        // ----------------------------------------------------------------

        private void UpdateAscending()
        {
            if (_rb == null)
                return;

            _rb.linearVelocity = Vector3.up * AscentSpeed;
            _rb.angularVelocity = Vector3.zero;
            RotateTowardVelocity();

            if (transform.position.y >= _apexY)
            {
                _rb.linearVelocity = Vector3.zero;
                _phase = Phase.Turning;
                IssaPluginPlugin.Log.LogInfo(
                    $"[Javelin] Apex reached at Y={transform.position.y:F1}. Turning."
                );
            }
        }

        private void UpdateTurning()
        {
            if (_rb == null)
                return;

            _rb.linearVelocity = Vector3.zero;
            _rb.angularVelocity = Vector3.zero;

            Vector3 toTarget = TargetPosition - transform.position;

            // If somehow the rocket is already on top of the target, dive straight down.
            if (toTarget.sqrMagnitude < 0.01f)
            {
                IssaPluginPlugin.Log.LogInfo("[Javelin] Already at target during turn — diving.");
                _phase = Phase.Diving;
                _currentDiveSpeed = DiveSpeed;
                return;
            }

            // Compute target rotation without any LookRotation call.
            // We slerp toward it at TurnRate deg/sec and advance phase once close.
            Quaternion targetRot = DirectionToRotation(toTarget.normalized);

            transform.rotation = Quaternion.RotateTowards(
                transform.rotation,
                targetRot,
                TurnRate * Time.fixedDeltaTime
            );

            if (Quaternion.Angle(transform.rotation, targetRot) < 5f)
            {
                _phase = Phase.Diving;
                _currentDiveSpeed = DiveSpeed;
                IssaPluginPlugin.Log.LogInfo("[Javelin] Turn complete — diving.");
            }
        }

        private void UpdateDiving()
        {
            if (_rb == null)
                return;

            _currentDiveSpeed += DiveAcceleration * Time.fixedDeltaTime;

            Vector3 toTarget = TargetPosition - transform.position;

            Vector3 velocity =
                toTarget.sqrMagnitude > 0.001f
                    ? toTarget.normalized * _currentDiveSpeed
                    : Vector3.down * _currentDiveSpeed;

            _rb.linearVelocity = velocity;
            RotateTowardVelocity();

            float dist3d = Vector3.Distance(transform.position, TargetPosition);
            float distXZ = Vector3.Distance(
                new Vector3(transform.position.x, 0f, transform.position.z),
                new Vector3(TargetPosition.x, 0f, TargetPosition.z)
            );

            bool veryClose = dist3d < ArrivalRadius;
            bool pastTarget =
                transform.position.y <= TargetPosition.y + 2f && distXZ < ArrivalRadius * 2f;

            if (veryClose || pastTarget)
                Detonate();
        }

        // ----------------------------------------------------------------
        //  Rotation helpers — never call LookRotation with zero or
        //  near-parallel vectors
        // ----------------------------------------------------------------

        /// Smoothly rotates the transform to face the current Rigidbody velocity.
        /// Skipped entirely when velocity is near-zero so LookRotation is never
        /// given a zero forward vector.
        private void RotateTowardVelocity()
        {
            if (_rb == null)
                return;

            Vector3 vel = _rb.linearVelocity;
            if (vel.sqrMagnitude < 0.01f)
                return; // stationary — keep current rotation, don't call LookRotation

            Quaternion target = DirectionToRotation(vel.normalized);
            transform.rotation = Quaternion.RotateTowards(
                transform.rotation,
                target,
                720f * Time.fixedDeltaTime
            ); // fast cosmetic snap
        }

        /// Converts a normalised direction into a Quaternion without ever passing
        /// a zero or near-parallel vector to Quaternion.LookRotation.
        ///
        /// Unity's LookRotation(forward, up) requires forward and up to be
        /// non-zero AND non-parallel.  When forward ≈ ±Vector3.up the cross
        /// product collapses, producing the "Look rotation viewing vector is zero"
        /// warning.  We detect this and substitute Vector3.forward as the up axis.
        private static Quaternion DirectionToRotation(Vector3 direction)
        {
            // direction is assumed to be normalised by the caller.
            // Guard against zero just in case.
            if (direction.sqrMagnitude < 0.001f)
                return Quaternion.identity;

            // Choose an up vector that is guaranteed not to be parallel to direction.
            // |dot| > 0.99 means the angle between direction and Vector3.up is < ~8°
            // (or > ~172°), i.e. the rocket is pointing nearly straight up or down.
            Vector3 up =
                Mathf.Abs(Vector3.Dot(direction, Vector3.up)) > 0.99f
                    ? Vector3.forward // use world-forward when nearly vertical
                    : Vector3.up;

            return Quaternion.LookRotation(direction, up);
        }

        // ----------------------------------------------------------------
        //  Detonation
        // ----------------------------------------------------------------

        public void Detonate()
        {
            if (_exploded)
                return;
            _exploded = true;

            if (_rocket != null)
                JavelinItem.ActiveJavelinRockets.Remove(_rocket);

            if (_rocket == null)
                return;

            Vector3 pos = transform.position;

            IssaPluginPlugin.Log.LogInfo(
                $"[Javelin] Detonating at {pos:F1} (target {TargetPosition:F1})."
            );

            // Broadcast explosion position to all clients so each can play the VFX.
            NetworkServer.SendToAll(new JavelinExplosionMessage { Position = pos });

            ServerExplodeMethod?.Invoke(_rocket, new object[] { pos });
        }
    }
}
