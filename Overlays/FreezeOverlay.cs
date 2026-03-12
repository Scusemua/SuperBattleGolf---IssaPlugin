using UnityEngine;

namespace IssaPlugin.Overlays
{
    /// <summary>
    /// Draws a frost vignette over the screen while the world is frozen.
    /// The texture is generated once in Start() as a smooth radial gradient:
    /// transparent at the center, icy white-blue at the edges.
    /// Added to the Plugin's persistent gameObject in Plugin.cs.
    /// </summary>
    public class FreezeOverlay : MonoBehaviour
    {
        public static FreezeOverlay Instance { get; private set; }

        private bool _frozen;
        private Texture2D _frostTexture;

        private void Awake()
        {
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
            if (_frostTexture != null)
                Destroy(_frostTexture);
        }

        private void Start()
        {
            _frostTexture = GenerateFrostTexture(256, 256);
        }

        public void SetFrozen(bool frozen)
        {
            _frozen = frozen;
        }

        private void OnGUI()
        {
            if (!_frozen || _frostTexture == null)
                return;

            GUI.DrawTexture(
                new Rect(0, 0, Screen.width, Screen.height),
                _frostTexture,
                ScaleMode.StretchToFill
            );
        }

        /// <summary>
        /// Generates a radial gradient texture: transparent in the center,
        /// icy blue-white at the edges, suitable for a frost vignette.
        /// </summary>
        private static Texture2D GenerateFrostTexture(int w, int h)
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

                    // Transparent center, opaque icy edges.
                    // Power of 1.85 gives a gradual ramp that fills screen corners.
                    float alpha = Mathf.Pow(Mathf.Clamp01(dist), 1.85f) * 0.75f;
                    pixels[y * w + x] = new Color(0.8f, 0.92f, 1.0f, alpha);
                }
            }

            tex.SetPixels(pixels);
            tex.Apply();
            return tex;
        }
    }
}
