using IssaPlugin;
using UnityEngine;
using UnityEngine.InputSystem;

namespace IssaPlugin.Overlays
{
    /// <summary>
    /// Debug overlay that simulates a "poisoned" visual effect. Press J to toggle.
    /// Not wired to any item yet — exists purely for visual prototyping.
    ///
    /// Effects:
    ///   - Camera Z-roll sway (compound sine, feels organic)
    ///   - Camera FOV breathing (slow pulse)
    ///   - Warm amber vignette with pulsing opacity
    ///   - Subtle full-screen amber tint
    ///   - Double-vision ghost (second camera → RenderTexture drawn offset)
    /// </summary>
    public class PoisonOverlay : MonoBehaviour
    {
        public static PoisonOverlay Instance { get; private set; }

        private bool _manualActive; // toggled by the J debug key
        private float _poisonEndTime = -1f; // realtimeSinceStartup when poison expires; -1 = not poisoned
        private bool _wasActive;
        private float _time; // elapsed seconds while active

        private bool IsActive => _manualActive || Time.realtimeSinceStartup < _poisonEndTime;

        // Camera state saved on activation so we can restore on deactivation
        private float _baseFov;
        private float _baseRollZ;
        private bool _camStateStored;

        // Textures
        private Texture2D _vignetteTex;
        private Texture2D _tintTex;

        // Ghost / double-vision
        private Camera _ghostCam;
        private RenderTexture _ghostTex;
        private int _ghostTexW,
            _ghostTexH;

        private const Key ToggleKey = Key.J;

        // ── Unity lifecycle ────────────────────────────────────────────────

        private void Awake() => Instance = this;

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
            RestoreCamera();
            DestroyGhostCamera();
            if (_vignetteTex != null)
                Destroy(_vignetteTex);
            if (_tintTex != null)
                Destroy(_tintTex);
        }

        // ── Input & timing ─────────────────────────────────────────────────

        /// <summary>
        /// Activates the poison visual on the local player for the given duration.
        /// If the effect is already running the end time is extended to whichever is later.
        /// Called by PoisonJarNetworkBridge.HandleLanded when the local player is within AoE.
        /// </summary>
        public void ActivatePoison(float duration)
        {
            float endTime = Time.realtimeSinceStartup + duration;
            if (endTime > _poisonEndTime)
                _poisonEndTime = endTime;
        }

        private void Update()
        {
            var kb = Keyboard.current;
            if (kb != null && kb[ToggleKey].wasPressedThisFrame)
                _manualActive = !_manualActive;

            bool active = IsActive;

            if (active && !_wasActive)
            {
                _time = 0f;
                _camStateStored = false;
            }
            else if (!active && _wasActive)
            {
                RestoreCamera();
                DestroyGhostCamera();
            }

            _wasActive = active;

            if (active)
                _time += Time.deltaTime;
        }

        // ── Camera effects (LateUpdate wins over game camera updates in Update) ──

        private void LateUpdate()
        {
            if (!IsActive)
                return;

            var cam = Camera.main;
            if (cam == null)
                return;

            // Capture baseline on the first active frame
            if (!_camStateStored)
            {
                _baseFov = cam.fieldOfView;
                _baseRollZ = cam.transform.eulerAngles.z;
                _camStateStored = true;
            }

            // Compound sine roll — multiple frequencies feel organic rather than mechanical
            float roll =
                _baseRollZ
                + Mathf.Sin(_time * Configuration.PoisonRollFreq1.Value)
                    * Configuration.PoisonRollAmp1.Value
                + Mathf.Sin(_time * Configuration.PoisonRollFreq2.Value)
                    * Configuration.PoisonRollAmp2.Value
                + Mathf.Sin(_time * Configuration.PoisonRollFreq3.Value)
                    * Configuration.PoisonRollAmp3.Value;

            Vector3 euler = cam.transform.eulerAngles;
            euler.z = roll;
            cam.transform.eulerAngles = euler;

            // FOV breathing
            float fovDelta =
                Mathf.Sin(_time * Configuration.PoisonFovFreq1.Value)
                    * Configuration.PoisonFovAmp1.Value
                + Mathf.Sin(_time * Configuration.PoisonFovFreq2.Value)
                    * Configuration.PoisonFovAmp2.Value;
            cam.fieldOfView = _baseFov + fovDelta;

            // Ghost camera — snap to main cam and render to texture
            if (Configuration.PoisonGhostEnabled.Value)
            {
                EnsureGhostCamera(cam);
                _ghostCam.transform.SetPositionAndRotation(
                    cam.transform.position,
                    cam.transform.rotation
                );
                _ghostCam.fieldOfView = cam.fieldOfView;
                _ghostCam.cullingMask = cam.cullingMask;
                _ghostCam.nearClipPlane = cam.nearClipPlane;
                _ghostCam.farClipPlane = cam.farClipPlane;
                _ghostCam.Render();
            }
        }

        private void RestoreCamera()
        {
            if (!_camStateStored)
                return;
            var cam = Camera.main;
            if (cam != null)
            {
                Vector3 euler = cam.transform.eulerAngles;
                euler.z = _baseRollZ;
                cam.transform.eulerAngles = euler;
                cam.fieldOfView = _baseFov;
            }
            _camStateStored = false;
        }

        // ── 2D overlay effects ─────────────────────────────────────────────

        private void OnGUI()
        {
            if (!IsActive)
                return;

            float sw = Screen.width;
            float sh = Screen.height;

            EnsureTextures();

            // Ghost image — drawn first so tint/vignette layer on top
            if (Configuration.PoisonGhostEnabled.Value && _ghostTex != null)
            {
                float offsetX = Configuration.PoisonGhostOffsetX.Value * sw;
                float offsetY = Configuration.PoisonGhostOffsetY.Value * sh;
                GUI.color = new Color(1f, 1f, 1f, Configuration.PoisonGhostAlpha.Value);
                GUI.DrawTexture(new Rect(offsetX, offsetY, sw, sh), _ghostTex);
                GUI.color = Color.white;
            }

            // Subtle full-screen amber tint — very low alpha, slow pulse
            if (_tintTex != null)
            {
                float tintAlpha =
                    Configuration.PoisonTintBaseAlpha.Value
                    + Configuration.PoisonTintPulseAmp.Value
                        * Mathf.Sin(_time * Configuration.PoisonTintPulseFreq.Value);
                GUI.color = new Color(1f, 1f, 1f, tintAlpha);
                GUI.DrawTexture(new Rect(0, 0, sw, sh), _tintTex);
                GUI.color = Color.white;
            }

            // Warm amber vignette with pulsing opacity
            if (_vignetteTex != null)
            {
                float vigAlpha =
                    Configuration.PoisonVigBaseAlpha.Value
                    + Configuration.PoisonVigPulseAmp1.Value
                        * Mathf.Sin(_time * Configuration.PoisonVigPulseFreq1.Value)
                    + Configuration.PoisonVigPulseAmp2.Value
                        * Mathf.Sin(_time * Configuration.PoisonVigPulseFreq2.Value);
                GUI.color = new Color(1f, 1f, 1f, vigAlpha);
                GUI.DrawTexture(new Rect(0, 0, sw, sh), _vignetteTex, ScaleMode.StretchToFill);
                GUI.color = Color.white;
            }
        }

        // ── Ghost camera helpers ───────────────────────────────────────────

        private void EnsureGhostCamera(Camera src)
        {
            int w = Screen.width,
                h = Screen.height;

            // Recreate RenderTexture if the screen size changed
            if (_ghostTex != null && (_ghostTexW != w || _ghostTexH != h))
            {
                _ghostCam.targetTexture = null;
                Destroy(_ghostTex);
                _ghostTex = null;
            }

            if (_ghostTex == null)
            {
                _ghostTex = new RenderTexture(w, h, 24);
                _ghostTexW = w;
                _ghostTexH = h;
                if (_ghostCam != null)
                    _ghostCam.targetTexture = _ghostTex;
            }

            if (_ghostCam == null)
            {
                var go = new GameObject("PoisonGhostCamera")
                {
                    hideFlags = HideFlags.HideAndDontSave,
                };
                _ghostCam = go.AddComponent<Camera>();
                _ghostCam.enabled = false; // manual Render() only
                _ghostCam.targetTexture = _ghostTex;
            }
        }

        private void DestroyGhostCamera()
        {
            if (_ghostCam != null)
            {
                _ghostCam.targetTexture = null;
                Destroy(_ghostCam.gameObject);
                _ghostCam = null;
            }
            if (_ghostTex != null)
            {
                Destroy(_ghostTex);
                _ghostTex = null;
            }
        }

        // ── Texture helpers ────────────────────────────────────────────────

        private void EnsureTextures()
        {
            if (_tintTex == null)
            {
                _tintTex = new Texture2D(1, 1);
                _tintTex.SetPixel(0, 0, new Color(0.9f, 0.5f, 0.1f, 1f));
                _tintTex.Apply();
            }

            if (_vignetteTex == null)
                _vignetteTex = GenerateGreenVignette(128, 128); // low-res stretched; bilinear hides it
        }

        /// <summary>
        /// Radial gradient: transparent center → warm amber edges.
        /// Generated at low resolution and stretched via bilinear filtering.
        /// </summary>
        private static Texture2D GenerateGreenVignette(int w, int h)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            float cx = w / 2f,
                cy = h / 2f;
            float maxDist = Mathf.Sqrt(cx * cx + cy * cy);

            var pixels = new Color[w * h];
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    float dist = Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy)) / maxDist;
                    float alpha = Mathf.Pow(Mathf.Clamp01(dist), 1.4f) * 0.8f;
                    pixels[y * w + x] = new Color(0.35f, 0.75f, 0.05f, alpha);
                }
            }

            tex.SetPixels(pixels);
            tex.filterMode = FilterMode.Bilinear;
            tex.Apply();
            return tex;
        }
    }
}
