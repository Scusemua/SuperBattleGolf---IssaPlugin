using System.Collections;
using System.Reflection;
using Mirror;
using UnityEngine;
using UnityEngine.InputSystem;

namespace IssaPlugin.Items
{
    public static class AC130Helpers
    {

        // ----------------------------------------------------------------
        //  Helpers
        // ----------------------------------------------------------------
        public static Vector3 OrbitPosition(
            Vector3 centre,
            float angleDeg,
            float radius,
            float altitude
        )
        {
            float rad = angleDeg * Mathf.Deg2Rad;
            return new Vector3(
                centre.x + Mathf.Cos(rad) * radius,
                centre.y + altitude,
                centre.z + Mathf.Sin(rad) * radius
            );
        }

        public static Vector3 OrbitTangent(float angleDeg)
        {
            // Derivative of OrbitPosition with respect to angle, normalised.
            float rad = angleDeg * Mathf.Deg2Rad;
            return new Vector3(-Mathf.Sin(rad), 0f, Mathf.Cos(rad)).normalized;
        }
    } // end of class
} // end of namespace
