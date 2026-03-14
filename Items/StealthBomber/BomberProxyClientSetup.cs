using UnityEngine;

namespace IssaPlugin.Items
{
    /// Added to BomberProxyPrefab in AssetLoader so every instance — both the
    /// server-side proxy and the client-side Mirror-spawned copy — automatically
    /// gets the components the lock-on system expects.
    ///
    /// Order matters: Entity must exist before LockOnTarget.Awake() runs so
    /// LockOnTarget can cache AsEntity = GetComponent<Entity>() as non-null.
    public class BomberProxyClientSetup : MonoBehaviour
    {
        private void Awake()
        {
            if (gameObject.GetComponent<BomberMarker>() == null)
                gameObject.AddComponent<BomberMarker>();

            if (gameObject.GetComponent<Entity>() == null)
                gameObject.AddComponent<Entity>();

            if (gameObject.GetComponent<LockOnTarget>() == null)
                gameObject.AddComponent<LockOnTarget>();
        }
    }
}
