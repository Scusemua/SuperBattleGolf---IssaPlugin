using UnityEngine;

namespace IssaPlugin.Overlays
{
    /// <summary>
    /// Draws a soft purple-violet vignette over the screen while low gravity is active,
    /// evoking a weightless, space-like feeling.
    /// The texture is generated once in Start() as a smooth radial gradient:
    /// transparent at the center, violet at the edges.
    /// Added to the Plugin's persistent gameObject in Plugin.cs.
    /// </summary>
    public class LowGravityOverlay : MonoBehaviour
    {
        public static LowGravityOverlay Instance { get; private set; }

        private bool _active;
        private Texture2D _vignetteTexture;

        private void Awake()
        {
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
            if (_vignetteTexture != null)
                Destroy(_vignetteTexture);
        }

        private void Start()
        {
            _vignetteTexture = GenerateVignetteTexture(256, 256);
        }

        public void SetActive(bool active)
        {
            _active = active;
        }

        private void OnGUI()
        {
            if (!_active || _vignetteTexture == null)
                return;

            GUI.DrawTexture(
                new Rect(0, 0, Screen.width, Screen.height),
                _vignetteTexture,
                ScaleMode.StretchToFill
            );
        }

        /// <summary>
        /// Generates a radial gradient texture: transparent in the center,
        /// soft purple-violet at the edges to evoke a space/low-gravity feel.
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
                    float dx = x - cx;
                    float dy = y - cy;
                    float dist = Mathf.Sqrt(dx * dx + dy * dy) / maxDist;

                    // Transparent center, soft violet at the edges.
                    float alpha = Mathf.Pow(Mathf.Clamp01(dist), 0.6f) * 0.55f;
                    pixels[y * w + x] = new Color(0.35f, 0.1f, 0.8f, alpha);
                }
            }

            tex.SetPixels(pixels);
            tex.Apply();
            return tex;
        }
    }
}
