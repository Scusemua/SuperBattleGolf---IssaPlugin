using System.Collections;
using System.Reflection;
using Mirror;
using UnityEngine;
using UnityEngine.InputSystem;

namespace IssaPlugin.Items
{
    // ----------------------------------------------------------------
    //  Gunship movement
    // ----------------------------------------------------------------
    public class AC130FlyBehaviour : MonoBehaviour
    {
        public Vector3 mapCentre;
        public float orbitRadius;
        public float altitude; // target altitude, set externally by HandleFlight
        public float orbitSpeed; // degrees per second
        public float currentAngle;
        public float altitudeLerpSpeed = 5f;

        private float _currentAltitude;

        private void Start()
        {
            _currentAltitude = altitude;
        }

        private void Update()
        {
            currentAngle += orbitSpeed * Time.deltaTime;
            _currentAltitude = Mathf.Lerp(
                _currentAltitude,
                altitude,
                altitudeLerpSpeed * Time.deltaTime
            );

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
    }
}
