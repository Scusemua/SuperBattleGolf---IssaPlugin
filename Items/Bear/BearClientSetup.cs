using UnityEngine;

namespace IssaPlugin.Items
{
    /// <summary>
    /// Added to the bear prefab in AssetLoader so both the server-spawned instance
    /// and every client's Mirror-spawned copy automatically get the components
    /// the lock-on and animation systems need.
    ///
    /// This runs in Awake() on ALL machines (server and every client) because it
    /// is baked onto the prefab — analogous to AC130ClientSetup and DonutClientSetup.
    ///
    /// The BearAnimatorDriver (client-side state visualisation) is added here.
    /// BearBehaviour (server-side AI) is added by BearNetworkBridge.ServerSummonBears()
    /// AFTER NetworkServer.Spawn so it only exists on the server.
    /// </summary>
    public class BearClientSetup : MonoBehaviour
    {
        private void Awake()
        {
            // Tag so GunshipLockOnPatches / OrbitalLaser patches can recognise bears
            if (gameObject.GetComponent<BearMarker>() == null)
                gameObject.AddComponent<BearMarker>();

            // Entity is required by the base game's LockOnTarget.Awake()
            if (gameObject.GetComponent<Entity>() == null)
                gameObject.AddComponent<Entity>();

            // Lock-on target so players can fire homing rockets at bears
            if (gameObject.GetComponent<LockOnTarget>() == null)
                gameObject.AddComponent<LockOnTarget>();

            // Client-side animator driver — reads BearStateMessages and
            // translates them into Animator parameter changes.
            // Safe to add on the server too; it just does nothing without a Camera.
            if (gameObject.GetComponent<BearAnimatorDriver>() == null)
                gameObject.AddComponent<BearAnimatorDriver>();
        }
    }
}
