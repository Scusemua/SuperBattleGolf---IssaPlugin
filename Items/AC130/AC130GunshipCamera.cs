using UnityEngine;
using UnityEngine.InputSystem;

namespace IssaPlugin.Items
{
    /// Gunship camera that rides with the AC130 as it orbits the map.
    ///
    /// Default look direction is straight toward the map centre from the
    /// gunship's current position.  The player can pan with the mouse within
    /// a clamped cone (yawLimit / pitchLimit degrees either side of centre).
    /// The crosshair is always at screen centre, so firing is a simple
    /// forward-raycast from the camera.
    ///
    /// Call <see cref="TriggerFireShake"/> each time a rocket is fired to
    /// produce a subtle recoil kick.
    public class AC130GunshipCamera : MonoBehaviour
    {
        // ----------------------------------------------------------------
        //  Setup — assigned by AC130Session before Activate()
        // ----------------------------------------------------------------

        /// The point the gunship orbits around.
        public Vector3 mapCentre;

        /// Base FOV when not zooming.
        public float baseFov = 60f;

        /// How many degrees left/right the player can pan from centre.
        public float yawLimit = 40f;

        /// How many degrees up/down the player can pan from centre.
        public float pitchLimit = 30f;

        /// Mouse sensitivity (degrees per pixel).
        public float mouseSensitivity = 0.15f;

        // ----------------------------------------------------------------
        //  Shake configuration
        // ----------------------------------------------------------------

        /// Peak angular kick in degrees (applied as a random offset).
        public float shakePeakDegrees = 1.4f;

        /// How quickly the shake decays to zero (higher = snappier).
        public float shakeDecaySpeed = 6f;

        // ----------------------------------------------------------------
        //  Runtime state
        // ----------------------------------------------------------------
        private Camera _gunshipCam;
        private Camera _previousCam;
        private bool _active;

        /// 
        /// Player's accumulated pan offset from the neutral (map-centre) direction,
        /// in degrees.  x = pitch offset (up/down), y = yaw offset (left/right).
        /// Reset to zero when Activate() is called.
        /// 
        private Vector2 _lookOffset;

        /// 
        /// Current shake rotation offset in degrees (x = pitch, y = yaw).
        /// Decays to zero each frame; set to a random kick by TriggerFireShake.
        /// 
        private Vector2 _shakeOffset;

        // ----------------------------------------------------------------
        //  Unity lifecycle
        // ----------------------------------------------------------------

        private void Awake()
        {
            var camGo = new GameObject("AC130_GunshipCam");
            camGo.transform.SetParent(transform, false);

            _gunshipCam = camGo.AddComponent<Camera>();
            _gunshipCam.fieldOfView = baseFov;
            _gunshipCam.nearClipPlane = 1f;
            _gunshipCam.farClipPlane = 8000f;
            _gunshipCam.enabled = false;
        }

        private void OnDestroy()
        {
            if (_active)
                Deactivate();
        }

        // ----------------------------------------------------------------
        //  Public API
        // ----------------------------------------------------------------

        public void Activate()
        {
            if (_active)
                return;

            _lookOffset = Vector2.zero;
            _shakeOffset = Vector2.zero;

            _previousCam = Camera.main;
            if (_previousCam != null)
                _previousCam.enabled = false;

            _gunshipCam.fieldOfView = baseFov;
            _gunshipCam.enabled = true;
            _active = true;

            ApplyCameraTransform();

            IssaPluginPlugin.Log.LogInfo("[AC130] Gunship camera activated.");
        }

        public void Deactivate()
        {
            if (!_active)
                return;

            _gunshipCam.enabled = false;

            if (_previousCam != null)
                _previousCam.enabled = true;

            _active = false;

            IssaPluginPlugin.Log.LogInfo("[AC130] Gunship camera deactivated.");
        }

        /// 
        /// Call every frame from the on-station loop to consume mouse delta
        /// and accumulate the look offset.
        /// 
        public void UpdateLook()
        {
            if (!_active)
                return;

            var mouse = Mouse.current;
            if (mouse == null)
                return;

            Vector2 delta = mouse.delta.ReadValue();

            _lookOffset.y += delta.x * mouseSensitivity;
            _lookOffset.x -= delta.y * mouseSensitivity;

            _lookOffset.x = Mathf.Clamp(_lookOffset.x, -pitchLimit, pitchLimit);
            _lookOffset.y = Mathf.Clamp(_lookOffset.y, -yawLimit, yawLimit);
        }

        /// 
        /// Triggers a small recoil shake. Call this each time a rocket fires.
        /// The shake is purely rotational and decays smoothly — it does not
        /// affect <see cref="_lookOffset"/> so the player's aim stays where
        /// they left it once the shake settles.
        /// 
        public void TriggerFireShake()
        {
            // Kick the camera upward (negative pitch = tilt up) with a tiny
            // random yaw wobble to feel less mechanical.
            _shakeOffset.x = -shakePeakDegrees;
            _shakeOffset.y = Random.Range(-shakePeakDegrees * 0.4f, shakePeakDegrees * 0.4f);
        }

        /// 
        /// Smoothly interpolates FOV toward <paramref name="targetFov"/>.
        /// 
        public void SetFov(float targetFov, float lerpSpeed)
        {
            if (_gunshipCam == null)
                return;

            _gunshipCam.fieldOfView = Mathf.Lerp(
                _gunshipCam.fieldOfView,
                targetFov,
                lerpSpeed * Time.deltaTime
            );
        }

        public float FieldOfView => _gunshipCam != null ? _gunshipCam.fieldOfView : baseFov;

        public Camera Camera => _gunshipCam;

        // ----------------------------------------------------------------
        //  Positioning
        // ----------------------------------------------------------------

        private void LateUpdate()
        {
            if (!_active)
                return;

            // Decay the shake offset toward zero each frame.
            _shakeOffset = Vector2.Lerp(
                _shakeOffset,
                Vector2.zero,
                shakeDecaySpeed * Time.deltaTime
            );

            ApplyCameraTransform();
        }

        private void ApplyCameraTransform()
        {
            if (_gunshipCam == null)
                return;

            // Sit the camera at the gunship's position, nudged slightly toward
            // the map centre so it doesn't clip through the plane geometry.
            Vector3 nudge = (mapCentre - transform.position).normalized * 3f;
            _gunshipCam.transform.position = transform.position + nudge;

            // ----------------------------------------------------------------
            //  Neutral look direction: gunship → map centre.
            // ----------------------------------------------------------------
            Vector3 toCenter = mapCentre - transform.position;

            if (toCenter.sqrMagnitude < 0.01f)
                toCenter = Vector3.down;

            Quaternion neutralRotation = Quaternion.LookRotation(toCenter.normalized, Vector3.up);

            // ----------------------------------------------------------------
            //  Layer: player mouse-look offset.
            //  Layer: shake offset (decays automatically, doesn't affect aim).
            // ----------------------------------------------------------------
            Vector2 totalOffset = _lookOffset + _shakeOffset;

            Quaternion yawOffset = Quaternion.AngleAxis(totalOffset.y, Vector3.up);
            Quaternion pitchOffset = Quaternion.AngleAxis(totalOffset.x, Vector3.right);

            _gunshipCam.transform.rotation = yawOffset * neutralRotation * pitchOffset;
        }
    }
}
