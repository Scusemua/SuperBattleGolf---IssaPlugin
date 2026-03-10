using Mirror;
using UnityEngine;
using UnityEngine.InputSystem;

namespace IssaPlugin.Items
{
    /// <summary>
    /// Attached to the gunship GameObject when a mayday is triggered.
    /// Drives the dive physics, cockpit camera (owning client only),
    /// camera shake, and crash impact.
    ///
    /// Lifecycle:
    ///   Server calls Begin() → sets up dive state, notifies all clients.
    ///   Each client creates its own instance (via RpcBeginMayday).
    ///   On the owning client, the cockpit camera is activated.
    ///   On impact, the server spawns the explosion, deals damage, then
    ///   Destroy()s the GameObject (which cleans up all clients via Mirror).
    /// </summary>
    public class AC130MaydayBehaviour : MonoBehaviour
    {
        // ----------------------------------------------------------------
        //  Set by whoever calls Begin()
        // ----------------------------------------------------------------

        /// True only on the owning client — enables cockpit cam + input.
        public bool IsLocalPlayer { get; set; }

        /// Assigned on all clients so the dive can aim toward map centre.
        public Vector3 MapCentre { get; set; }

        /// Callback invoked on the server when the gunship hits the ground.
        /// The server bridge subscribes to this to run explosion / cleanup.
        public System.Action OnImpact { get; set; }

        // ----------------------------------------------------------------
        //  Dive state
        // ----------------------------------------------------------------

        private float _diveAngle; // degrees below horizontal, increases over time
        private float _rollAngle; // cumulative player roll, degrees
        private float _driftVelX; // random lateral drift accumulator
        private float _driftVelZ;
        private bool _impacted;

        // ----------------------------------------------------------------
        //  Cockpit camera (owning client only)
        // ----------------------------------------------------------------

        private Camera _cockpitCam;
        private Camera _previousCam;
        private Vector2 _lookOffset; // player mouse-look accumulation
        private Vector2 _shakeOffset; // decaying shake, separate from look

        // ----------------------------------------------------------------
        //  Smoke trail (all clients)
        // ----------------------------------------------------------------

        private GameObject _smokeTrail;

        // ----------------------------------------------------------------
        //  Unity lifecycle
        // ----------------------------------------------------------------

        private void Start()
        {
            _diveAngle = Configuration.AC130MaydayInitialDiveAngle.Value;
            _rollAngle = 0f;

            // Seed a small random lateral drift so each mayday looks slightly different.
            float drift = Configuration.AC130MaydayDrift.Value;
            _driftVelX = Random.Range(-drift, drift);
            _driftVelZ = Random.Range(-drift, drift);

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

            // Cockpit camera — owning client only.
            if (IsLocalPlayer)
                SetupCockpitCamera();

            // Mayday alarm — owning client only.
            if (IsLocalPlayer)
                PlayMaydayAlarm();
        }

        private void OnDestroy()
        {
            // Ensure cockpit camera is always restored even if something
            // destroys the GameObject before Impact() runs normally.
            if (IsLocalPlayer && _cockpitCam != null)
                DeactivateCockpitCamera();

            if (_smokeTrail != null)
                Destroy(_smokeTrail);
        }

        private void Update()
        {
            if (_impacted)
                return;

            UpdateDive();

            if (IsLocalPlayer)
                UpdateCockpitLook();

            ApplyCameraTransform();
            UpdateCameraShake();

            // Ground check — server triggers the impact.
            if (NetworkServer.active)
                CheckImpact();
        }

        // ----------------------------------------------------------------
        //  Dive physics
        // ----------------------------------------------------------------

        private void UpdateDive()
        {
            var keyboard = Keyboard.current;

            // Steepen the dive over time.
            float steepRate = Configuration.AC130MaydayDiveSteepRate.Value;
            float maxAngle = Configuration.AC130MaydayMaxDiveAngle.Value;
            _diveAngle = Mathf.MoveTowards(_diveAngle, maxAngle, steepRate * Time.deltaTime);

            // Player pull influence (owning client sends no cmd — input is
            // processed locally and only affects visual/local state; the
            // server runs the same Update so physics stay in sync via
            // Mirror's NetworkTransform on the gunship).
            if (IsLocalPlayer && keyboard != null)
            {
                float pull = Configuration.AC130MaydayPullInfluence.Value;
                if (keyboard[Key.W].isPressed || keyboard[Key.UpArrow].isPressed)
                    _diveAngle -= pull * Time.deltaTime;
                if (keyboard[Key.S].isPressed || keyboard[Key.DownArrow].isPressed)
                    _diveAngle += pull * Time.deltaTime;

                _diveAngle = Mathf.Clamp(_diveAngle, 5f, maxAngle);

                float rollSpeed = Configuration.AC130MaydayRollSpeed.Value;
                if (keyboard[Key.A].isPressed || keyboard[Key.LeftArrow].isPressed)
                    _rollAngle -= rollSpeed * Time.deltaTime;
                if (keyboard[Key.D].isPressed || keyboard[Key.RightArrow].isPressed)
                    _rollAngle += rollSpeed * Time.deltaTime;
            }

            // Build forward direction: pitched down by _diveAngle, biased
            // toward map centre, plus random drift.
            Vector3 toCenter = (MapCentre - transform.position);
            toCenter.y = 0f;
            if (toCenter.sqrMagnitude < 0.01f)
                toCenter = transform.forward;
            toCenter.Normalize();

            // Blend current forward with "toward centre" so it gradually
            // curves that way without being a hard lock.
            Vector3 horizontalDir = Vector3.Lerp(
                new Vector3(transform.forward.x, 0f, transform.forward.z).normalized,
                toCenter,
                2f * Time.deltaTime
            );
            horizontalDir.Normalize();

            // Add drift.
            horizontalDir.x += _driftVelX * Time.deltaTime * 0.1f;
            horizontalDir.z += _driftVelZ * Time.deltaTime * 0.1f;
            horizontalDir.Normalize();

            // Compose final direction: horizontal component pitched down.
            float pitchRad = _diveAngle * Mathf.Deg2Rad;
            Vector3 diveDir = new Vector3(
                horizontalDir.x * Mathf.Cos(pitchRad),
                -Mathf.Sin(pitchRad),
                horizontalDir.z * Mathf.Cos(pitchRad)
            ).normalized;

            // Apply roll to up-vector.
            Vector3 right = Vector3.Cross(Vector3.up, diveDir).normalized;
            Vector3 rolledUp = Quaternion.AngleAxis(_rollAngle, diveDir) * Vector3.up;

            // Move and orient.
            float speed = Configuration.AC130MaydaySpeed.Value;
            transform.position += diveDir * speed * Time.deltaTime;
            if (diveDir.sqrMagnitude > 0.01f)
                transform.rotation = Quaternion.LookRotation(diveDir, rolledUp);
        }

        // ----------------------------------------------------------------
        //  Impact (server only)
        // ----------------------------------------------------------------

        private void CheckImpact()
        {
            // Simple ground check: y <= 0 or raycast hit within one frame's travel.
            float speed = Configuration.AC130MaydaySpeed.Value;
            float checkDist = speed * Time.deltaTime * 2f;

            bool hitGround = transform.position.y <= 0f;
            if (!hitGround)
            {
                hitGround = Physics.Raycast(
                    transform.position,
                    transform.forward,
                    checkDist,
                    AC130Item.GroundLayerMask
                );
            }

            if (hitGround)
                Impact();
        }

        private void Impact()
        {
            if (_impacted)
                return;

            _impacted = true;

            IssaPluginPlugin.Log.LogInfo($"[Mayday] Impact at {transform.position}.");
            OnImpact?.Invoke();

            // The bridge's OnImpact callback destroys the GameObject, which
            // cleans up all clients via Mirror. Don't Destroy here directly.
        }

        // ----------------------------------------------------------------
        //  Cockpit camera (owning client only)
        // ----------------------------------------------------------------

        private void SetupCockpitCamera()
        {
            var camGo = new GameObject("AC130_CockpitCam");
            camGo.transform.SetParent(transform, false);

            // Position the camera at the cockpit — slightly forward and up
            // from the model's pivot so it sits where a pilot would be.
            camGo.transform.localPosition = new Vector3(0f, 1.5f, 2f);

            _cockpitCam = camGo.AddComponent<Camera>();
            _cockpitCam.fieldOfView = 75f;
            _cockpitCam.nearClipPlane = 0.3f;
            _cockpitCam.farClipPlane = 8000f;

            _previousCam = Camera.main;
            if (_previousCam != null)
                _previousCam.enabled = false;

            _cockpitCam.enabled = true;
            _lookOffset = Vector2.zero;
            _shakeOffset = Vector2.zero;

            IssaPluginPlugin.Log.LogInfo("[Mayday] Cockpit camera activated.");
        }

        private void DeactivateCockpitCamera()
        {
            if (_cockpitCam != null)
                _cockpitCam.enabled = false;

            if (_previousCam != null)
                _previousCam.enabled = true;

            IssaPluginPlugin.Log.LogInfo("[Mayday] Cockpit camera deactivated.");
        }

        private void UpdateCockpitLook()
        {
            if (_cockpitCam == null)
                return;

            var mouse = Mouse.current;
            if (mouse == null)
                return;

            float sens = Configuration.AC130MouseSensitivity.Value;
            Vector2 delta = mouse.delta.ReadValue();
            _lookOffset.y += delta.x * sens;
            _lookOffset.x -= delta.y * sens;

            float yawLim = Configuration.AC130MaydayCamYawLimit.Value;
            float pitchLim = Configuration.AC130MaydayCamPitchLimit.Value;
            _lookOffset.x = Mathf.Clamp(_lookOffset.x, -pitchLim, pitchLim);
            _lookOffset.y = Mathf.Clamp(_lookOffset.y, -yawLim, yawLim);
        }

        private void UpdateCameraShake()
        {
            if (_cockpitCam == null)
                return;

            // Shake intensity increases as dive angle steepens.
            float maxAngle = Configuration.AC130MaydayMaxDiveAngle.Value;
            float t = Mathf.Clamp01(_diveAngle / maxAngle);
            float shakeBase = Configuration.AC130MaydayShakeBase.Value;
            float shakeMax = Configuration.AC130MaydayShakeMax.Value;
            float intensity = Mathf.Lerp(shakeBase, shakeMax, t);

            _shakeOffset = new Vector2(
                Mathf.Lerp(
                    _shakeOffset.x,
                    Random.Range(-intensity, intensity),
                    20f * Time.deltaTime
                ),
                Mathf.Lerp(
                    _shakeOffset.y,
                    Random.Range(-intensity, intensity),
                    20f * Time.deltaTime
                )
            );
        }

        private void ApplyCameraTransform()
        {
            if (_cockpitCam == null || !IsLocalPlayer)
                return;

            // Neutral direction: cockpit faces forward along the gunship.
            Quaternion baseRot = transform.rotation;

            // Layer player look offset on top.
            Vector2 total = _lookOffset + _shakeOffset;
            Quaternion yaw = Quaternion.AngleAxis(total.y, transform.up);
            Quaternion pitch = Quaternion.AngleAxis(total.x, transform.right);

            _cockpitCam.transform.rotation = yaw * pitch * baseRot;
        }

        // ----------------------------------------------------------------
        //  Audio
        // ----------------------------------------------------------------

        private void PlayMaydayAlarm()
        {
            var clip = AssetLoader.MaydayAlarmClip;
            if (clip == null)
            {
                IssaPluginPlugin.Log.LogWarning("[Mayday] Alarm clip not loaded.");
                return;
            }

            var go = new GameObject("Mayday_Alarm");
            var src = go.AddComponent<AudioSource>();
            src.clip = clip;
            src.loop = true;
            src.spatialBlend = 0f;
            src.volume = 1f;
            src.Play();

            // Tied to the gunship so it stops automatically when it's destroyed.
            go.transform.SetParent(transform, false);
        }
    }
}
