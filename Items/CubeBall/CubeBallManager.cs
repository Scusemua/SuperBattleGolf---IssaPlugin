using UnityEngine;

namespace IssaPlugin.Items
{
    /// Persistent singleton MonoBehaviour that hosts all server-side cube-ball
    /// coroutines.  Lives on the plugin root GameObject (added in Plugin.cs) so
    /// it survives any player disconnect and remains alive for the full session.
    public class CubeBallManager : MonoBehaviour
    {
        public static CubeBallManager Instance { get; private set; }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }
    }
}
