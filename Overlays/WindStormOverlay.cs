using IssaPlugin.Items;
using UnityEngine;

namespace IssaPlugin.Overlays
{
    /// <summary>
    /// Draws a time-remaining bar (and a subtle stormy vignette) while a Wind Storm is active.
    /// Visible to ALL players — the storm is a global world effect, not a per-player buff.
    ///
    /// Slot assignment: above any active Freeze / LowGravity bars, so the three global
    /// world-effect bars stack without overlapping.  Per-player bars (Jetpack, Spinach)
    /// check WindStormOverlay.IsActive and step up one additional slot.
    ///
    /// Added to the Plugin's persistent GameObject in Plugin.cs.
    /// </summary>
    public class WindStormOverlay : MonoBehaviour
    {
        public static WindStormOverlay Instance { get; private set; }

        // ── Effect state ──────────────────────────────────────────────────
        private bool _active;
        private float _startTime;
        private float _duration;

        /// Checked by other overlay slot calculations each frame.
        public static bool IsActive => Instance != null && Instance._active;

        // ── Textures ─────────────────────────────────────────────────────
        private Texture2D _vignetteTexture;
        private Texture2D _barBgTexture;
        private Texture2D _barFillTexture;
        private int _cachedBarW = -1;

        // Stormy teal-grey fill — distinct from Freeze blue, LowGravity purple, Jetpack orange
        private static readonly Color FillColor = new Color(0.35f, 0.65f, 0.85f, 0.9f);
        private static readonly Color BgColor = new Color(0f, 0f, 0f, 0.55f);

        // Lazily initialised inside OnGUI (GUI.skin only valid during GUI events)
        private GUIStyle _labelStyle;

        // ── Unity lifecycle ───────────────────────────────────────────────

        private void Awake() => Instance = this;

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
            DestroyBarTextures();
            if (_vignetteTexture != null)
                Destroy(_vignetteTexture);
        }

        private void Start()
        {
            _vignetteTexture = GenerateStormVignetteTexture(256, 256);
        }

        // ── Public API ────────────────────────────────────────────────────

        public void SetActive(bool active, float duration = 0f)
        {
            _active = active;
            if (active)
            {
                _duration = duration;
                _startTime = Time.time;
            }
        }

        // ── Rendering ─────────────────────────────────────────────────────

        private void OnGUI()
        {
            if (!_active)
                return;

            // Subtle grey-blue vignette to hint at stormy conditions
            if (_vignetteTexture != null)
                GUI.DrawTexture(
                    new Rect(0, 0, Screen.width, Screen.height),
                    _vignetteTexture,
                    ScaleMode.StretchToFill
                );

            if (_duration <= 0f)
                return;

            int barWInt = (int)EffectBarLayout.GetBarWidth();
            if (barWInt != _cachedBarW)
                RebuildBarTextures(barWInt);

            if (_barBgTexture == null || _barFillTexture == null)
                return;

            float elapsed = Time.time - _startTime;
            float fraction = Mathf.Clamp01(1f - elapsed / _duration);
            float remaining = Mathf.Max(0f, _duration - elapsed);

            float barW = EffectBarLayout.GetBarWidth();
            float barH = EffectBarLayout.BarHeight;
            float barX = EffectBarLayout.GetBarX();

            // Stack above any active Freeze / LowGravity bars
            int slot = (FreezeItem.IsFrozen ? 1 : 0) + (LowGravityItem.IsActive ? 1 : 0);
            float barY = EffectBarLayout.GetBarY(slot);

            GUI.DrawTexture(new Rect(barX, barY, barW, barH), _barBgTexture);

            GUI.BeginGroup(new Rect(barX, barY, barW * fraction, barH));
            GUI.DrawTexture(new Rect(0, 0, barW, barH), _barFillTexture);
            GUI.EndGroup();

            _labelStyle ??= new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 13,
                fontStyle = FontStyle.Bold,
            };
            _labelStyle.normal.textColor = Color.white;
            GUI.Label(
                new Rect(barX, barY, barW, barH),
                $"Wind Storm Time Remaining:  {remaining:F1}s",
                _labelStyle
            );
        }

        // ── Texture helpers ───────────────────────────────────────────────

        private void RebuildBarTextures(int barW)
        {
            DestroyBarTextures();
            int barH = (int)EffectBarLayout.BarHeight;
            int radius = barH / 3;
            _barBgTexture = GenerateRoundedRectTexture(barW, barH, radius, BgColor);
            _barFillTexture = GenerateRoundedRectTexture(barW, barH, radius, FillColor);
            _cachedBarW = barW;
        }

        private void DestroyBarTextures()
        {
            if (_barBgTexture != null)
            {
                Destroy(_barBgTexture);
                _barBgTexture = null;
            }
            if (_barFillTexture != null)
            {
                Destroy(_barFillTexture);
                _barFillTexture = null;
            }
            _cachedBarW = -1;
        }

        private static Texture2D GenerateRoundedRectTexture(int w, int h, int radius, Color color)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            var pixels = new Color[w * h];
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                pixels[y * w + x] = IsInsideRoundedRect(x, y, w, h, radius) ? color : Color.clear;
            tex.SetPixels(pixels);
            tex.filterMode = FilterMode.Bilinear;
            tex.Apply();
            return tex;
        }

        private static bool IsInsideRoundedRect(int x, int y, int w, int h, int r)
        {
            bool inL = x < r,
                inR = x >= w - r,
                inT = y < r,
                inB = y >= h - r;
            if (!((inL || inR) && (inT || inB)))
                return true;
            int cx = inL ? r : (w - 1 - r);
            int cy = inT ? r : (h - 1 - r);
            float dx = x - cx,
                dy = y - cy;
            return dx * dx + dy * dy <= r * r;
        }

        /// <summary>
        /// Radial gradient: transparent centre → dark grey-blue edges, evoking overcast skies.
        /// </summary>
        private static Texture2D GenerateStormVignetteTexture(int w, int h)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            float cx = w * 0.5f,
                cy = h * 0.5f;
            float maxD = Mathf.Sqrt(cx * cx + cy * cy);
            var pixels = new Color[w * h];
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float dist = Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy)) / maxD;
                float alpha = Mathf.Pow(Mathf.Clamp01(dist), 0.7f) * 0.35f;
                pixels[y * w + x] = new Color(0.2f, 0.3f, 0.45f, alpha);
            }
            tex.SetPixels(pixels);
            tex.Apply();
            return tex;
        }
    }
}
