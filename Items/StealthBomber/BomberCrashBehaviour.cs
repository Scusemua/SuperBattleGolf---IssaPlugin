using UnityEngine;

namespace IssaPlugin.Items
{
    /// Attached to the local visual bomber GameObject on all clients when the
    /// bomber is shot down (via BomberNetworkBridge.RpcBomberShotDown).
    ///
    /// The Rigidbody is already live and moving when this is added — this
    /// component only watches for ground impact (y <= 0) and triggers the
    /// explosion. A safety timeout destroys the object if it never reaches y=0
    /// (e.g. terrain is above sea level and the bomber hits a hillside).
    public class BomberCrashBehaviour : MonoBehaviour
    {
        private bool _impacted;
        private float _lifetime;

        private const float MaxLifetime = 15f;

        private void FixedUpdate()
        {
            if (_impacted)
                return;

            _lifetime += Time.deltaTime;

            if (transform.position.y <= 0f || _lifetime >= MaxLifetime)
                Impact();
        }

        private void Impact()
        {
            if (_impacted)
                return;

            _impacted = true;

            IssaPluginPlugin.Log.LogInfo($"[BomberCrash] Impact at {transform.position}.");

            VfxManager.PlayPooledVfxLocalOnly(
                VfxType.RocketLauncherRocketExplosion,
                transform.position,
                Quaternion.identity,
                Vector3.one * Configuration.AC130MaydayExplosionScale.Value
            );

            CameraModuleController.Shake(
                GameManager.CameraGameplaySettings.RocketExplosionScreenshakeSettings,
                transform.position
            );

            Destroy(gameObject);
        }
    }
}
