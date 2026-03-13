using UnityEngine;

namespace IssaPlugin.Items
{
    /// <summary>
    /// Attached to the local visual bomber GameObject on all clients when the
    /// bomber is shot down (via BomberNetworkBridge.RpcBomberShotDown).
    /// Drives a simple crash dive: steepens the pitch over time, moves forward
    /// along the dive direction, then triggers an explosion on ground impact.
    ///
    /// BomberFlyBehaviour is disabled before this is added so only one driver
    /// moves the GameObject each frame.
    /// </summary>
    public class BomberCrashBehaviour : MonoBehaviour
    {
        private float _diveAngle; // degrees below horizontal; grows each frame
        private bool _impacted;

        private const float SteepRate = 60f; // deg/sec
        private const float MaxAngle = 80f;  // max dive angle

        private void Update()
        {
            if (_impacted)
                return;

            _diveAngle = Mathf.MoveTowards(_diveAngle, MaxAngle, SteepRate * Time.deltaTime);

            float pitchRad = _diveAngle * Mathf.Deg2Rad;
            Vector3 horizontal = new Vector3(
                transform.forward.x,
                0f,
                transform.forward.z
            ).normalized;

            if (horizontal.sqrMagnitude < 0.001f)
                horizontal = Vector3.forward;

            Vector3 diveDir = new Vector3(
                horizontal.x * Mathf.Cos(pitchRad),
                -Mathf.Sin(pitchRad),
                horizontal.z * Mathf.Cos(pitchRad)
            ).normalized;

            float speed = Configuration.BomberSpeed.Value;
            transform.position += diveDir * speed * Time.deltaTime;
            transform.rotation = Quaternion.LookRotation(diveDir, Vector3.up);

            if (transform.position.y <= 0f)
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
