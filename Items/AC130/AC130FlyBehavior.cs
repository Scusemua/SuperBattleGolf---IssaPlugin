using UnityEngine;

namespace IssaPlugin.Items
{
    public enum AC130FlightMode
    {
        FlyIn,
        Orbit,
        FlyOut,
    }

    public class AC130FlyBehaviour : MonoBehaviour
    {
        public Vector3 mapCentre;
        public float orbitRadius;
        public float altitude;
        public float orbitSpeed;
        public float currentAngle;
        public float altitudeLerpSpeed = 5f;

        public AC130FlightMode mode = AC130FlightMode.Orbit;
        public Vector3 flyTarget;
        public float flySpeed = 80f;

        public bool HasArrived { get; private set; }

        /// <summary>
        /// Assigned by AC130NetworkBridge on the server so external
        /// destruction can notify the bridge to trigger mayday.
        /// Only meaningful on the server.
        /// </summary>
        public System.Action OnExternallyDestroyed { get; set; }

        private float _currentAltitude;
        private const float AltitudeSnapThreshold = 0.01f;
        private const float ArrivalThreshold = 5f;
        private const float FlyOutDestroyDistance = 2000f;

        private Vector3 _flyOutStart;

        /// <summary>
        /// Set to true by BeginFlyOut() so OnDestroy knows this was a
        /// normal fly-out completion and should not trigger mayday.
        /// </summary>
        private bool _normalFlyOutComplete;

        private void Start()
        {
            _currentAltitude = altitude;
            if (mode == AC130FlightMode.FlyOut)
                _flyOutStart = transform.position;
        }

        private void Update()
        {
            switch (mode)
            {
                case AC130FlightMode.FlyIn:
                    UpdateFlyIn();
                    break;
                case AC130FlightMode.Orbit:
                    UpdateOrbit();
                    break;
                case AC130FlightMode.FlyOut:
                    UpdateFlyOut();
                    break;
            }
        }

        private void UpdateFlyIn()
        {
            transform.position = Vector3.MoveTowards(
                transform.position,
                flyTarget,
                flySpeed * Time.deltaTime
            );

            Vector3 dir = (flyTarget - transform.position).normalized;
            if (dir != Vector3.zero)
                transform.rotation = Quaternion.LookRotation(dir, Vector3.up);

            if (Vector3.Distance(transform.position, flyTarget) < ArrivalThreshold)
            {
                HasArrived = true;
                mode = AC130FlightMode.Orbit;
                _currentAltitude = altitude;
            }
        }

        private void UpdateOrbit()
        {
            currentAngle += orbitSpeed * Time.deltaTime;
            _currentAltitude = Mathf.Lerp(
                _currentAltitude,
                altitude,
                altitudeLerpSpeed * Time.deltaTime
            );

            if (Mathf.Abs(_currentAltitude - altitude) < AltitudeSnapThreshold)
                _currentAltitude = altitude;

            float rad = currentAngle * Mathf.Deg2Rad;
            transform.position = new Vector3(
                mapCentre.x + Mathf.Cos(rad) * orbitRadius,
                mapCentre.y + _currentAltitude,
                mapCentre.z + Mathf.Sin(rad) * orbitRadius
            );

            Vector3 tangent = new Vector3(-Mathf.Sin(rad), 0f, Mathf.Cos(rad)).normalized;
            if (tangent != Vector3.zero)
                transform.rotation = Quaternion.LookRotation(tangent, Vector3.up);
        }

        private void UpdateFlyOut()
        {
            transform.position += transform.forward * flySpeed * Time.deltaTime;

            if (Vector3.Distance(transform.position, _flyOutStart) > FlyOutDestroyDistance)
            {
                _normalFlyOutComplete = true;
                Destroy(gameObject);
            }
        }

        public void BeginFlyOut()
        {
            mode = AC130FlightMode.FlyOut;
            _flyOutStart = transform.position;
            flySpeed = Configuration.AC130ApproachSpeed.Value;
        }

        private void OnDestroy()
        {
            // Only notify mayday if destruction was external (not a normal fly-out).
            // We check _normalFlyOutComplete rather than mode == FlyOut so that
            // a gunship shot down *during* fly-out also doesn't trigger mayday
            // (fly-out = the session is already ending, player has their camera back).
            if (!_normalFlyOutComplete && mode != AC130FlightMode.FlyOut)
                OnExternallyDestroyed?.Invoke();
        }
    }
}
