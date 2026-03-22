using Mirror;
using UnityEngine;

namespace IssaPlugin.Items
{
    /// Server-side component attached to every spawned wall.
    /// Tracks melee hit count and triggers destruction when the threshold is met
    /// or when a rocket explodes nearby.
    public class PlaceableWallBehaviour : MonoBehaviour
    {
        /// Set by PlaceableWallNetworkBridge immediately after spawning so the
        /// wall can remove itself from the bridge's tracking list on destruction.
        public PlaceableWallNetworkBridge OwnerBridge { get; set; }
    }
}
