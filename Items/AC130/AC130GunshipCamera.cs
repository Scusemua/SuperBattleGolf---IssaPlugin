using UnityEngine;
using UnityEngine.InputSystem;

namespace IssaPlugin.Items
{
    /// <summary>
    /// Gunship camera that rides with the AC130 as it orbits the map.
    ///
    /// Default look direction is straight toward the map centre from the
    /// gunship's current position.  The player can pan with the mouse within
    /// a clamped cone (yawLimit / pitchLimit degrees either side of centre).
    /// The crosshair is always at screen centre, so firing is a simple
    /// forward-raycast from the camera.
    /// </summary>
    public class AC130GunshipCamera : MonoBehaviour
    {
        // ----------------------------------------------------------------
        //  Setup — assigned by AC130Session before Activate()
        // ----------------------------------------------------------------

        /// <summary>The point the gunship orbits around.</summary>
        public Vector3 mapCentre;

        /// <summary>Base FOV when not zooming.</summary>
        public float baseFov = 60f;

        /// <summary>How many degrees left/right the player can pan from centre.</summary>
        public float yawLimit = 40f;

        /// <summary>How many degrees up/down the player can pan from centre.</summary>
        public float pitchLimit = 30f;

        /// <summary>Mouse sensitivity (degrees per pixel).</summary>
        public float mouseSensitivity = 0.15f;

        // ----------------------------------------------------------------
        //  Runtime state
        // ----------------------------------------------------------------
        private Camera _gunshipCam;
        private Camera _previousCam;
        private bool _active;

        /// <summary>
        /// Player's accumulated pan offset from the neutral (map-centre) direction,
        /// in degrees.  x = pitch offset (up/down), y = yaw offset (left/right).
        /// Reset to zero when Activate() is called.
        /// </summary>
        private Vector2 _lookOffset;

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

            _previousCam = Camera.main;
            if (_previousCam != null)
                _previousCam.enabled = false;

            _gunshipCam.fieldOfView = baseFov;
            _gunshipCam.enabled = true;
            _active = true;

            // Snap to the correct orientation immediately.
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

        /// <summary>
        /// Call every frame from the on-station loop to consume mouse delta
        /// and accumulate the look offset.
        /// </summary>
        public void UpdateLook()
        {
            if (!_active)
                return;

            var mouse = Mouse.current;
            if (mouse == null)
                return;

            Vector2 delta = mouse.delta.ReadValue();

            // delta.x → yaw (left/right), delta.y → pitch (inverted so up = up).
            _lookOffset.y += delta.x * mouseSensitivity;
            _lookOffset.x -= delta.y * mouseSensitivity;

            _lookOffset.x = Mathf.Clamp(_lookOffset.x, -pitchLimit, pitchLimit);
            _lookOffset.y = Mathf.Clamp(_lookOffset.y, -yawLimit, yawLimit);
        }

        /// <summary>
        /// Smoothly interpolates FOV toward <paramref name="targetFov"/>.
        /// </summary>
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

        /// <summary>
        /// The underlying Camera — use this for crosshair raycasts.
        /// Screen-centre is always the aim point, so just fire a ray from
        /// the camera's position along its forward vector instead of using
        /// ScreenPointToRay with the mouse position.
        /// </summary>
        public Camera Camera => _gunshipCam;

        // ----------------------------------------------------------------
        //  Positioning — runs after AC130FlyBehaviour has moved the gunship
        // ----------------------------------------------------------------

        private void LateUpdate()
        {
            if (!_active)
                return;

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
            //  Apply player's mouse-look offset on top of the neutral rotation.
            //  Yaw rotates around world-up so left/right always feels horizontal.
            //  Pitch rotates around the camera's local right axis.
            // ----------------------------------------------------------------
            Quaternion yawOffset = Quaternion.AngleAxis(_lookOffset.y, Vector3.up);
            Quaternion pitchOffset = Quaternion.AngleAxis(_lookOffset.x, Vector3.right);

            _gunshipCam.transform.rotation = yawOffset * neutralRotation * pitchOffset;
        }
    }
}
