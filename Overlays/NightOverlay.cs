using IssaPlugin.Items;
using UnityEngine;

namespace IssaPlugin.Overlays
{
    /// Countdown bar for Night Time. No screen tint. The fill is neutral white.
    /// Stacks above Freeze, Low Gravity, and Wind Storm.
    public class NightOverlay : MonoBehaviour
    {
        public static NightOverlay Instance { get; private set; }

        public static bool IsActive => Instance != null && Instance._active;

        private bool _active;
        private float _startTime;
        private float _duration;

        private Texture2D _barBgTexture;
        private Texture2D _barFillTexture;
        private int _cachedBarW = -1;

        private static readonly Color FillColor = new Color(1f, 1f, 1f, 0.9f);
        private static readonly Color BgColor = new Color(0f, 0f, 0f, 0.55f);

        private GUIStyle _labelStyle;

        private void Awake() => Instance = this;

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
            DestroyBarTextures();
        }

        private void Update()
        {
            if (NightItem.IsActive)
                NightLighting.Maintain();
        }

        public void SetActive(bool active, float duration = 0f)
        {
            _active = active;
            if (active)
            {
                _duration = duration;
                _startTime = Time.time;
            }
        }

        private void OnGUI()
        {
            if (!_active || _duration <= 0f)
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
            int slot =
                (FreezeItem.IsFrozen ? 1 : 0)
                + (LowGravityItem.IsActive ? 1 : 0)
                + (WindStormOverlay.IsActive ? 1 : 0);
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
            var label = $"Night Time Remaining:  {remaining:F1}s";
            var labelRect = new Rect(barX, barY, barW, barH);
            // Dark copy under the white label so it stays readable on the white fill.
            var previous = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.85f);
            GUI.Label(new Rect(barX + 1f, barY + 1f, barW, barH), label, _labelStyle);
            GUI.color = previous;
            GUI.Label(labelRect, label, _labelStyle);
        }

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
    }
}
