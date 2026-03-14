using UnityEngine;

namespace IssaPlugin.Items
{
    /// <summary>
    /// Added to UFOPrefab in AssetLoader so both the server-spawned instance
    /// and every client's Mirror-spawned copy automatically gets the components
    /// the lock-on system expects.
    ///
    /// Order matters: Entity must be added before LockOnTarget.Awake() so that
    /// LockOnTarget can cache AsEntity = GetComponent&lt;Entity&gt;() as non-null.
    /// </summary>
    public class UFOClientSetup : MonoBehaviour
    {
        private void Awake()
        {
            if (gameObject.GetComponent<Entity>() == null)
                gameObject.AddComponent<Entity>();

            if (gameObject.GetComponent<LockOnTarget>() == null)
                gameObject.AddComponent<LockOnTarget>();
        }
    }
}
