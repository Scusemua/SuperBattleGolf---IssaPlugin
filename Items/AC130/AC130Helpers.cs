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

        /// <summary>
        /// Returns the midpoint between the hole and the first active tee platform,
        /// projected onto the XZ plane of the hole (Y taken from the hole).
        /// Falls back to <paramref name="fallback"/> if either landmark is unavailable.
        /// </summary>
        public static Vector3 ComputeMapCenter(Vector3 fallback)
        {
            var hole = GolfHoleManager.MainHole;
            if (hole == null)
                return fallback;

            Vector3 holePos = hole.transform.position;

            var teePlatforms = GolfTeeManager.ActiveTeeingPlaforms;
            if (teePlatforms == null || teePlatforms.Count == 0)
                return holePos;

            // Average across all active tee platforms for a more stable centre on
            // multi-tee holes, then midpoint with the hole.
            Vector3 teeSum = Vector3.zero;
            int count = 0;
            foreach (var tee in teePlatforms)
            {
                if (tee != null)
                {
                    teeSum += tee.transform.position;
                    count++;
                }
            }
            if (count == 0)
                return holePos;

            Vector3 teeCentroid = teeSum / count;
            return (holePos + teeCentroid) * 0.5f;
        }
    } // end of class
} // end of namespace
