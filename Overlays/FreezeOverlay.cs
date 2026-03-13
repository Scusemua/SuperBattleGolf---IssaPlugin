using UnityEngine;

namespace IssaPlugin.Overlays
{
    /// <summary>
    /// Draws a frost vignette and a time-remaining bar while the world is frozen.
    /// Bar textures are generated at screen-pixel resolution so rounded corners
    /// are never distorted by scaling. They are regenerated when Screen.width changes.
    /// Added to the Plugin's persistent gameObject in Plugin.cs.
    /// </summary>
    public class FreezeOverlay : MonoBehaviour
    {
        public static FreezeOverlay Instance { get; private set; }

        // ── Effect state ──────────────────────────────────────────────────
        private bool  _frozen;
        private float _startTime;
        private float _duration;

        // ── Textures ─────────────────────────────────────────────────────
        private Texture2D _frostTexture;
        private Texture2D _barBgTexture;
        private Texture2D _barFillTexture;
        private int       _cachedBarW = -1; // screen width for which bar textures were built

        // Icy blue fill colour
        private static readonly Color FillColor = new Color(0.3f, 0.7f, 1.0f, 0.9f);
        private static readonly Color BgColor   = new Color(0f,   0f,   0f,   0.55f);

        // Lazily initialised inside OnGUI (GUI.skin only valid during GUI events)
        private GUIStyle _labelStyle;

        // ── Unity lifecycle ───────────────────────────────────────────────

        private void Awake() => Instance = this;

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            DestroyBarTextures();
            if (_frostTexture != null) Destroy(_frostTexture);
        }

        private void Start()
        {
            _frostTexture = GenerateFrostTexture(256, 256);
        }

        // ── Public API ────────────────────────────────────────────────────

        /// <param name="frozen">Whether the freeze effect is active.</param>
        /// <param name="duration">Total duration in seconds (required when frozen = true).</param>
        public void SetFrozen(bool frozen, float duration = 0f)
        {
            _frozen = frozen;
            if (frozen)
            {
                _duration  = duration;
                _startTime = Time.time;
            }
        }

        // ── Rendering ─────────────────────────────────────────────────────

        private void OnGUI()
        {
            if (!_frozen)
                return;

            // Frost vignette
            if (_frostTexture != null)
                GUI.DrawTexture(
                    new Rect(0, 0, Screen.width, Screen.height),
                    _frostTexture,
                    ScaleMode.StretchToFill
                );

            if (_duration <= 0f)
                return;

            // Rebuild bar textures if the screen width changed
            int barWInt = (int)EffectBarLayout.GetBarWidth();
            if (barWInt != _cachedBarW)
                RebuildBarTextures(barWInt);

            if (_barBgTexture == null || _barFillTexture == null)
                return;

            float elapsed   = Time.time - _startTime;
            float fraction  = Mathf.Clamp01(1f - elapsed / _duration);
            float remaining = Mathf.Max(0f, _duration - elapsed);

            float barW = EffectBarLayout.GetBarWidth();
            float barH = EffectBarLayout.BarHeight;
            float barX = EffectBarLayout.GetBarX();
            float barY = EffectBarLayout.GetBarY(slot: 0); // Freeze is always the bottommost bar

            // Background rounded track (full width)
            GUI.DrawTexture(new Rect(barX, barY, barW, barH), _barBgTexture);

            // Coloured fill — clipped to fill width so the right edge depletes cleanly
            // while the left rounded corners stay intact
            GUI.BeginGroup(new Rect(barX, barY, barW * fraction, barH));
            GUI.DrawTexture(new Rect(0, 0, barW, barH), _barFillTexture);
            GUI.EndGroup();

            // Centred label
            _labelStyle ??= new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize  = 13,
                fontStyle = FontStyle.Bold,
            };
            _labelStyle.normal.textColor = Color.white;
            GUI.Label(new Rect(barX, barY, barW, barH), $"FREEZE WORLD  {remaining:F1}s", _labelStyle);
        }

        // ── Texture helpers ───────────────────────────────────────────────

        private void RebuildBarTextures(int barW)
        {
            DestroyBarTextures();
            int barH   = (int)EffectBarLayout.BarHeight;
            int radius = barH / 3;
            _barBgTexture   = GenerateRoundedRectTexture(barW, barH, radius, BgColor);
            _barFillTexture = GenerateRoundedRectTexture(barW, barH, radius, FillColor);
            _cachedBarW     = barW;
        }

        private void DestroyBarTextures()
        {
            if (_barBgTexture   != null) { Destroy(_barBgTexture);   _barBgTexture   = null; }
            if (_barFillTexture != null) { Destroy(_barFillTexture); _barFillTexture = null; }
            _cachedBarW = -1;
        }

        /// <summary>
        /// Generates a texture shaped as a rounded rectangle.
        /// Pixels outside the rounded corners are fully transparent.
        /// </summary>
        private static Texture2D GenerateRoundedRectTexture(int w, int h, int radius, Color color)
        {
            var tex    = new Texture2D(w, h, TextureFormat.RGBA32, false);
            var pixels = new Color[w * h];

            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    pixels[y * w + x] = IsInsideRoundedRect(x, y, w, h, radius) ? color : Color.clear;

            tex.SetPixels(pixels);
            tex.filterMode = FilterMode.Bilinear;
            tex.Apply();
            return tex;
        }

        /// <summary>
        /// Returns true if pixel (x, y) falls inside a rounded rectangle of the given corner radius.
        /// </summary>
        private static bool IsInsideRoundedRect(int x, int y, int w, int h, int r)
        {
            bool inLeftZone  = x < r;
            bool inRightZone = x >= w - r;
            bool inTopZone   = y < r;
            bool inBotZone   = y >= h - r;

            // Not in any corner zone — always inside
            if (!((inLeftZone || inRightZone) && (inTopZone || inBotZone)))
                return true;

            // Find the centre of the nearest corner circle and check distance
            int cx = inLeftZone ? r : (w - 1 - r);
            int cy = inTopZone  ? r : (h - 1 - r);

            float dx = x - cx;
            float dy = y - cy;
            return dx * dx + dy * dy <= r * r;
        }

        /// <summary>
        /// Radial gradient: transparent center → icy blue-white edges.
        /// </summary>
        private static Texture2D GenerateFrostTexture(int w, int h)
        {
            var tex    = new Texture2D(w, h, TextureFormat.RGBA32, false);
            float cx   = w * 0.5f;
            float cy   = h * 0.5f;
            float maxD = Mathf.Sqrt(cx * cx + cy * cy);

            var pixels = new Color[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float dist  = Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy)) / maxD;
                    float alpha = Mathf.Pow(Mathf.Clamp01(dist), 0.5f) * 0.825f;
                    pixels[y * w + x] = new Color(0.1f, 0.25f, 1.0f, alpha);
                }

            tex.SetPixels(pixels);
            tex.Apply();
            return tex;
        }
    }
}
