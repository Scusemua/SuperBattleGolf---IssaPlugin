using Mirror;
using UnityEngine;

namespace IssaPlugin.Items
{
    /// Server-side component attached to every spawned wall root.
    /// Destroys the root (pillars, glued bricks) once all destructible chunks
    /// have been deformed, using the same debris lifetime delay so everything
    /// disappears at the same time.
    public class PlaceableWallBehaviour : MonoBehaviour
    {
        /// Set by PlaceableWallNetworkBridge immediately after spawning so the
        /// wall can remove itself from the bridge's tracking list on destruction.
        public PlaceableWallNetworkBridge OwnerBridge { get; set; }

        private int _activeChunks;

        private void Start()
        {
            _activeChunks = GetComponentsInChildren<PlaceableWallDestructionBehaviour>().Length;
        }

        internal void OnChunkDeformed()
        {
            _activeChunks--;
            if (_activeChunks <= 0)
                Destroy(gameObject, Configuration.PlaceableWallDebrisLifetime.Value);
        }
    }
}
