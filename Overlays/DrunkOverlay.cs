using IssaPlugin;
using UnityEngine;
using UnityEngine.InputSystem;

namespace IssaPlugin.Overlays
{
    /// <summary>
    /// Debug overlay that simulates a "drunk" visual effect. Press J to toggle.
    /// Not wired to any item yet — exists purely for visual prototyping.
    ///
    /// Effects:
    ///   - Camera Z-roll sway (compound sine, feels organic)
    ///   - Camera FOV breathing (slow pulse)
    ///   - Warm amber vignette with pulsing opacity
    ///   - Subtle full-screen amber tint
    /// </summary>
    public class DrunkOverlay : MonoBehaviour
    {
        public static DrunkOverlay Instance { get; private set; }

        private bool _active;
        private float _time; // elapsed seconds while active

        // Camera state saved on activation so we can restore on deactivation
        private float _baseFov;
        private float _baseRollZ;
        private bool _camStateStored;

        // Textures
        private Texture2D _vignetteTex;
        private Texture2D _tintTex;

        private const Key ToggleKey = Key.J;

        // ── Unity lifecycle ────────────────────────────────────────────────

        private void Awake() => Instance = this;

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            RestoreCamera();
            if (_vignetteTex != null) Destroy(_vignetteTex);
            if (_tintTex != null) Destroy(_tintTex);
        }

        // ── Input & timing ─────────────────────────────────────────────────

        private void Update()
        {
            var kb = Keyboard.current;
            if (kb != null && kb[ToggleKey].wasPressedThisFrame)
            {
                _active = !_active;
                if (_active)
                {
                    _time = 0f;
                    _camStateStored = false; // re-capture FOV/roll on next LateUpdate
                }
                else
                {
                    RestoreCamera();
                }
            }

            if (_active)
                _time += Time.deltaTime;
        }

        // ── Camera effects (LateUpdate wins over game camera updates in Update) ──

        private void LateUpdate()
        {
            if (!_active) return;

            var cam = Camera.main;
            if (cam == null) return;

            // Capture baseline on the first active frame
            if (!_camStateStored)
            {
                _baseFov = cam.fieldOfView;
                _baseRollZ = cam.transform.eulerAngles.z;
                _camStateStored = true;
            }

            // Compound sine roll — multiple frequencies feel organic rather than mechanical
            float roll = _baseRollZ
                       + Mathf.Sin(_time * Configuration.DrunkRollFreq1.Value) * Configuration.DrunkRollAmp1.Value
                       + Mathf.Sin(_time * Configuration.DrunkRollFreq2.Value) * Configuration.DrunkRollAmp2.Value
                       + Mathf.Sin(_time * Configuration.DrunkRollFreq3.Value) * Configuration.DrunkRollAmp3.Value;

            Vector3 euler = cam.transform.eulerAngles;
            euler.z = roll;
            cam.transform.eulerAngles = euler;

            // FOV breathing
            float fovDelta = Mathf.Sin(_time * Configuration.DrunkFovFreq1.Value) * Configuration.DrunkFovAmp1.Value
                           + Mathf.Sin(_time * Configuration.DrunkFovFreq2.Value) * Configuration.DrunkFovAmp2.Value;
            cam.fieldOfView = _baseFov + fovDelta;
        }

        private void RestoreCamera()
        {
            if (!_camStateStored) return;
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
            if (!_active) return;

            float sw = Screen.width;
            float sh = Screen.height;

            EnsureTextures();

            // Subtle full-screen amber tint — very low alpha, slow pulse
            if (_tintTex != null)
            {
                float tintAlpha = Configuration.DrunkTintBaseAlpha.Value + Configuration.DrunkTintPulseAmp.Value * Mathf.Sin(_time * Configuration.DrunkTintPulseFreq.Value);
                GUI.color = new Color(1f, 1f, 1f, tintAlpha);
                GUI.DrawTexture(new Rect(0, 0, sw, sh), _tintTex);
                GUI.color = Color.white;
            }

            // Warm amber vignette with pulsing opacity
            if (_vignetteTex != null)
            {
                float vigAlpha = Configuration.DrunkVigBaseAlpha.Value
                               + Configuration.DrunkVigPulseAmp1.Value * Mathf.Sin(_time * Configuration.DrunkVigPulseFreq1.Value)
                               + Configuration.DrunkVigPulseAmp2.Value * Mathf.Sin(_time * Configuration.DrunkVigPulseFreq2.Value);
                GUI.color = new Color(1f, 1f, 1f, vigAlpha);
                GUI.DrawTexture(new Rect(0, 0, sw, sh), _vignetteTex, ScaleMode.StretchToFill);
                GUI.color = Color.white;
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
                _vignetteTex = GenerateWarmVignette(128, 128); // low-res stretched; bilinear hides it
        }

        /// <summary>
        /// Radial gradient: transparent center → warm amber edges.
        /// Generated at low resolution and stretched via bilinear filtering.
        /// </summary>
        private static Texture2D GenerateWarmVignette(int w, int h)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            float cx = w / 2f, cy = h / 2f;
            float maxDist = Mathf.Sqrt(cx * cx + cy * cy);

            var pixels = new Color[w * h];
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    float dist = Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy)) / maxDist;
                    float alpha = Mathf.Pow(Mathf.Clamp01(dist), 1.4f) * 0.8f;
                    pixels[y * w + x] = new Color(0.75f, 0.35f, 0.05f, alpha);
                }
            }

            tex.SetPixels(pixels);
            tex.filterMode = FilterMode.Bilinear;
            tex.Apply();
            return tex;
        }
    }
}
