using UnityEngine;

namespace IssaPlugin.Overlays
{
    /// <summary>
    /// Draws a soft purple-violet vignette and a time-remaining bar while low gravity is active.
    /// Added to the Plugin's persistent gameObject in Plugin.cs.
    /// </summary>
    public class LowGravityOverlay : MonoBehaviour
    {
        public static LowGravityOverlay Instance { get; private set; }

        private bool _active;
        private float _startTime;
        private float _duration;

        private Texture2D _vignetteTexture;
        private Texture2D _barBgTexture;
        private Texture2D _barFillTexture;

        // Lazily initialised inside OnGUI (GUI.skin is only valid during GUI events).
        private GUIStyle _labelStyle;

        private void Awake()
        {
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
            if (_vignetteTexture != null)  Destroy(_vignetteTexture);
            if (_barBgTexture != null)     Destroy(_barBgTexture);
            if (_barFillTexture != null)   Destroy(_barFillTexture);
        }

        private void Start()
        {
            _vignetteTexture = GenerateVignetteTexture(256, 256);
            _barBgTexture    = CreateSolidTexture(new Color(0f, 0f, 0f, 0.55f));
            _barFillTexture  = CreateSolidTexture(new Color(0.5f, 0.15f, 0.9f, 0.9f));
        }

        /// <param name="active">Whether the low-gravity effect is active.</param>
        /// <param name="duration">Total duration in seconds (only used when active = true).</param>
        public void SetActive(bool active, float duration = 0f)
        {
            _active = active;
            if (active)
            {
                _duration  = duration;
                _startTime = Time.time;
            }
        }

        private void OnGUI()
        {
            if (!_active)
                return;

            // Vignette
            if (_vignetteTexture != null)
                GUI.DrawTexture(
                    new Rect(0, 0, Screen.width, Screen.height),
                    _vignetteTexture,
                    ScaleMode.StretchToFill
                );

            // Time-remaining bar
            if (_duration > 0f && _barBgTexture != null && _barFillTexture != null)
            {
                float elapsed   = Time.time - _startTime;
                float fraction  = Mathf.Clamp01(1f - elapsed / _duration);
                float remaining = Mathf.Max(0f, _duration - elapsed);

                float barW = Screen.width * 0.5f;
                const float barH = 24f;
                float barX = (Screen.width  - barW) * 0.5f;
                float barY =  Screen.height - 56f;

                // Background track
                GUI.DrawTexture(new Rect(barX, barY, barW, barH), _barBgTexture);

                // Coloured fill, depleting left-to-right
                GUI.DrawTexture(new Rect(barX, barY, barW * fraction, barH), _barFillTexture);

                // Text label centred on the bar
                _labelStyle ??= new GUIStyle(GUI.skin.label)
                {
                    alignment = TextAnchor.MiddleCenter,
                    fontSize   = 13,
                    fontStyle  = FontStyle.Bold,
                };
                _labelStyle.normal.textColor = Color.white;

                GUI.Label(
                    new Rect(barX, barY, barW, barH),
                    $"LOW GRAVITY  {remaining:F1}s",
                    _labelStyle
                );
            }
        }

        /// <summary>
        /// Radial gradient: transparent center → soft purple-violet edges.
        /// </summary>
        private static Texture2D GenerateVignetteTexture(int w, int h)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            float cx = w * 0.5f;
            float cy = h * 0.5f;
            float maxDist = Mathf.Sqrt(cx * cx + cy * cy);

            var pixels = new Color[w * h];
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    float dx   = x - cx;
                    float dy   = y - cy;
                    float dist = Mathf.Sqrt(dx * dx + dy * dy) / maxDist;

                    float alpha = Mathf.Pow(Mathf.Clamp01(dist), 0.6f) * 0.55f;
                    pixels[y * w + x] = new Color(0.35f, 0.1f, 0.8f, alpha);
                }
            }

            tex.SetPixels(pixels);
            tex.Apply();
            return tex;
        }

        private static Texture2D CreateSolidTexture(Color color)
        {
            var tex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            tex.SetPixel(0, 0, color);
            tex.Apply();
            return tex;
        }
    }
}
