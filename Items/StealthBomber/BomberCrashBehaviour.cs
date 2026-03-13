using UnityEngine;
using UnityEngine.InputSystem;

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

        private float _speed;

        private const float MaxLifetime = 15f;

        private GameObject _smokeTrail;
        private GameObject _fireTrail;

        public Rigidbody Rigidbody { get; set; }

        public void Start()
        {
            _speed = Configuration.AC130MaydaySpeed.Value;

            // Smoke trail — all clients spawn it locally (purely visual).
            if (AssetLoader.MaydaySmokeTrailPrefab != null)
            {
                _smokeTrail = Instantiate(
                    AssetLoader.MaydaySmokeTrailPrefab,
                    transform.position,
                    Quaternion.identity
                );
                _smokeTrail.transform.SetParent(transform, worldPositionStays: true);
            }
            else
            {
                IssaPluginPlugin.Log.LogWarning("[Mayday] Smoke trail prefab not loaded.");
            }

            // Smoke trail — all clients spawn it locally (purely visual).
            if (AssetLoader.MaydayFireTrailPrefab != null)
            {
                _fireTrail = Instantiate(
                    AssetLoader.MaydayFireTrailPrefab,
                    transform.position,
                    Quaternion.identity
                );
                _fireTrail.transform.SetParent(transform, worldPositionStays: true);
            }
            else
            {
                IssaPluginPlugin.Log.LogWarning("[Mayday] Fire trail prefab not loaded.");
            }
        }

        private void FixedUpdate()
        {
            if (_impacted)
                return;

            _lifetime += Time.deltaTime;

            if (CheckImpact())
                Impact();
        }

        private bool CheckImpact()
        {
            if (Rigidbody == null)
            {
                return false;
            }

            float speed = Vector3.Magnitude(Rigidbody.linearVelocity);
            float checkDist = speed * Time.deltaTime * 2f;

            bool hitGround = transform.position.y <= 0f;
            if (!hitGround)
            {
                hitGround = Physics.Raycast(
                    transform.position,
                    transform.forward,
                    checkDist,
                    ItemHelper.GroundLayerMask
                );
            }

            return hitGround;
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
            Destroy(_smokeTrail);
            Destroy(_fireTrail);
        }

        private void OnDestroy()
        {
            Destroy(gameObject);
            Destroy(_smokeTrail);
            Destroy(_fireTrail);
        }
    }
}
