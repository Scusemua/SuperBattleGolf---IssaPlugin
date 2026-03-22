using UnityEngine;

namespace IssaPlugin.Items
{
    /// <summary>
    /// Added to HarrierPrefab in AssetLoader so every spawned instance — both the
    /// server-side jet and the client-side Mirror-replicated copy — automatically
    /// gets the components the lock-on system expects.
    ///
    /// Component order matters: Entity must exist before LockOnTarget.Awake() runs
    /// so LockOnTarget can cache AsEntity = GetComponent&lt;Entity&gt;() as non-null.
    /// </summary>
    public class HarrierClientSetup : MonoBehaviour
    {
        private void Awake()
        {
            if (gameObject.GetComponent<HarrierMarker>() == null)
                gameObject.AddComponent<HarrierMarker>();

            if (gameObject.GetComponent<Entity>() == null)
                gameObject.AddComponent<Entity>();

            if (gameObject.GetComponent<LockOnTarget>() == null)
                gameObject.AddComponent<LockOnTarget>();
        }
    }
}
