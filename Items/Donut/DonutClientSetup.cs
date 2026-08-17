using UnityEngine;

namespace IssaPlugin.Items
{
    /// <summary>
    /// Added to DonutPrefab in AssetLoader so both the server-spawned instance
    /// and every client's Mirror-spawned copy automatically gets the components
    /// the lock-on system expects.
    ///
    /// Order matters: Entity must be added before LockOnTarget.Awake() so that
    /// LockOnTarget can cache AsEntity = GetComponent&lt;Entity&gt;() as non-null.
    /// </summary>
    public class DonutClientSetup : MonoBehaviour
    {
        private void Awake()
        {
            if (gameObject.GetComponent<DonutMarker>() == null)
                gameObject.AddComponent<DonutMarker>();

            // See AC130ClientSetup: refresh the orbital laser's marker cache so this
            // donut is targetable immediately rather than after the rescan interval.
            Patches.OrbitalLaserAircraftHelpers.Invalidate();

            if (gameObject.GetComponent<Entity>() == null)
                gameObject.AddComponent<Entity>();

            if (gameObject.GetComponent<LockOnTarget>() == null)
                gameObject.AddComponent<LockOnTarget>();
        }
    }
}
