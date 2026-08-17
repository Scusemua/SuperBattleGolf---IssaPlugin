using UnityEngine;

namespace IssaPlugin.Items
{
    /// Added to AC130Prefab in AssetLoader so every instance — both the server-side
    /// gunship and the client-side Mirror-spawned copy — automatically gets the
    /// components the lock-on system expects.
    ///
    /// Order matters: Entity must exist before LockOnTarget.Awake() runs so
    /// LockOnTarget can cache AsEntity = GetComponent<Entity>() as non-null.
    public class AC130ClientSetup : MonoBehaviour
    {
        private void Awake()
        {
            if (gameObject.GetComponent<AC130GunshipMarker>() == null)
                gameObject.AddComponent<AC130GunshipMarker>();

            // The orbital laser caches aircraft markers and only rescans on an
            // interval. Tell it a marker just appeared so targeting picks this
            // gunship up on the next frame rather than up to RescanInterval later.
            Patches.OrbitalLaserAircraftHelpers.Invalidate();

            if (gameObject.GetComponent<Entity>() == null)
                gameObject.AddComponent<Entity>();

            if (gameObject.GetComponent<LockOnTarget>() == null)
                gameObject.AddComponent<LockOnTarget>();
        }
    }
}
