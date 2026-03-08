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
        public float altitude;
        public float orbitSpeed; // degrees per second
        public float currentAngle;

        private void Update()
        {
            currentAngle += orbitSpeed * Time.deltaTime;

            float rad = currentAngle * Mathf.Deg2Rad;
            transform.position = new Vector3(
                mapCentre.x + Mathf.Cos(rad) * orbitRadius,
                mapCentre.y + altitude,
                mapCentre.z + Mathf.Sin(rad) * orbitRadius
            );

            // Always face the tangent direction of the circle.
            Vector3 tangent = new Vector3(-Mathf.Sin(rad), 0f, Mathf.Cos(rad)).normalized;
            if (tangent != Vector3.zero)
                transform.rotation = Quaternion.LookRotation(tangent, Vector3.up);
        }
    }
}
