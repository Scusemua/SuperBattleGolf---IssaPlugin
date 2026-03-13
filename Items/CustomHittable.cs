using IssaPlugin;
using Mirror;
using UnityEngine;

namespace IssaPlugin.Items
{
    public class CustomHittable : MonoBehaviour
    {
        public int HitCount { get; protected set; }

        public int HitsRequired { get; protected set; }

        /// Invoked on the server when hit count reaches the threshold.
        public System.Action OnHitsExceeded { get; set; }

        /// Invoked on the server whenever a hit is detected.
        public System.Action OnHit { get; set; }
    }
}
