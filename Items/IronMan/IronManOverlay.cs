using IssaPlugin.Items;
using UnityEngine;

namespace IssaPlugin.Overlays
{
    /// <summary>
    /// HUD for the Iron Man suit: shows a session timer bar and remaining rocket count.
    /// Only visible on the local player's screen while the session is active.
    /// Added to the plugin's persistent GameObject in Plugin.cs.
    ///
    /// Visibility is driven entirely by IronManItem.SessionActive so the overlay
    /// disappears as soon as the flight loop ends, regardless of whether
    /// IronManSuitEndMessage has arrived yet.
    /// </summary>
    public class IronManOverlay : MonoBehaviour
    {
        public static IronManOverlay Instance { get; private set; }

        // ── Push-based state (used for HUD values only, NOT for visibility) ───
        private static float _sessionDuration;
        private static int   _maxRockets;

        // IsActive intentionally reads IronManItem directly so the overlay
        // disappears the moment the local flight loop ends, without waiting for
        // the server SuitEnd message to round-trip.
        public static bool IsActive => IronManItem.SessionActive;

        public static void OnSessionStart(IronManConfigMessage cfg)
        {
            _sessionDuration = cfg.Duration;
            _maxRockets      = cfg.MaxRockets;
        }

        public static void OnSessionEnd() { /* nothing — IsActive reads IronManItem */ }

        public static void OnAmmoUpdate(int _) { /* IronManItem.RocketsRemaining is authoritative */ }

        // ── Colours ───────────────────────────────────────────────────────────
        private static readonly Color TimerFillColor = new Color(0.8f, 0.1f, 0.1f, 0.9f);
        private static readonly Color BgColor        = new Color(0f,   0f,   0f,   0.55f);
        private static readonly Color AmmoColor      = new Color(1.0f, 0.75f, 0.0f, 0.9f);

        // ── Textures (lazily built) ───────────────────────────────────────────
        private Texture2D _barBgTex;
        private Texture2D _timerFillTex;
        private Texture2D _ammoFillTex;
        private int _cachedBarW = -1;

        private GUIStyle _labelStyle;

        // ── Unity lifecycle ───────────────────────────────────────────────────

        private void Awake() => Instance = this;

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            DestroyTextures();
        }

        // ── Rendering ─────────────────────────────────────────────────────────

        private void OnGUI()
        {
            if (!IsActive) return;

            int barWInt = (int)EffectBarLayout.GetBarWidth();
            if (barWInt != _cachedBarW) RebuildTextures(barWInt);
            if (_barBgTex == null) return;

            float barW = EffectBarLayout.GetBarWidth();
            float barH = EffectBarLayout.BarHeight;
            float barX = EffectBarLayout.GetBarX();

            int slot =
                (FreezeItem.IsFrozen      ? 1 : 0)
                + (LowGravityItem.IsActive  ? 1 : 0)
                + (WindStormOverlay.IsActive ? 1 : 0);

            // ── Timer bar ─────────────────────────────────────────────────────
            float timeRemaining = IronManItem.SessionTimeRemaining;
            float timeFraction  = _sessionDuration > 0f
                ? Mathf.Clamp01(timeRemaining / _sessionDuration)
                : 0f;

            float timerY = EffectBarLayout.GetBarY(slot);
            GUI.DrawTexture(new Rect(barX, timerY, barW, barH), _barBgTex);
            GUI.BeginGroup(new Rect(barX, timerY, barW * timeFraction, barH));
            GUI.DrawTexture(new Rect(0, 0, barW, barH), _timerFillTex);
            GUI.EndGroup();

            _labelStyle ??= new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize  = 13,
                fontStyle = FontStyle.Bold,
            };
            _labelStyle.normal.textColor = Color.white;

            GUI.Label(
                new Rect(barX, timerY, barW, barH),
                $"Iron Man  {timeRemaining:F1}s",
                _labelStyle
            );

            // ── Ammo bar ──────────────────────────────────────────────────────
            int  rocketsRemaining = IronManItem.RocketsRemaining;
            float ammoFraction = _maxRockets > 0
                ? Mathf.Clamp01((float)rocketsRemaining / _maxRockets)
                : 0f;

            float ammoY = EffectBarLayout.GetBarY(slot + 1);
            GUI.DrawTexture(new Rect(barX, ammoY, barW, barH), _barBgTex);
            GUI.BeginGroup(new Rect(barX, ammoY, barW * ammoFraction, barH));
            GUI.DrawTexture(new Rect(0, 0, barW, barH), _ammoFillTex);
            GUI.EndGroup();

            GUI.Label(
                new Rect(barX, ammoY, barW, barH),
                $"Wrist Rockets  {rocketsRemaining}/{_maxRockets}",
                _labelStyle
            );
        }

        // ── Texture helpers ───────────────────────────────────────────────────

        private void RebuildTextures(int barW)
        {
            DestroyTextures();
            int barH   = (int)EffectBarLayout.BarHeight;
            int radius = barH / 3;
            _barBgTex     = MakeRoundedRect(barW, barH, radius, BgColor);
            _timerFillTex = MakeRoundedRect(barW, barH, radius, TimerFillColor);
            _ammoFillTex  = MakeRoundedRect(barW, barH, radius, AmmoColor);
            _cachedBarW   = barW;
        }

        private void DestroyTextures()
        {
            if (_barBgTex != null)     { Destroy(_barBgTex);     _barBgTex     = null; }
            if (_timerFillTex != null) { Destroy(_timerFillTex); _timerFillTex = null; }
            if (_ammoFillTex != null)  { Destroy(_ammoFillTex);  _ammoFillTex  = null; }
            _cachedBarW = -1;
        }

        private static Texture2D MakeRoundedRect(int w, int h, int r, Color color)
        {
            var tex    = new Texture2D(w, h, TextureFormat.RGBA32, false);
            var pixels = new Color[w * h];
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                pixels[y * w + x] = Inside(x, y, w, h, r) ? color : Color.clear;
            tex.SetPixels(pixels);
            tex.filterMode = FilterMode.Bilinear;
            tex.Apply();
            return tex;
        }

        private static bool Inside(int x, int y, int w, int h, int r)
        {
            bool L = x < r, R = x >= w - r, T = y < r, B = y >= h - r;
            if (!((L || R) && (T || B))) return true;
            int cx = L ? r : (w - 1 - r);
            int cy = T ? r : (h - 1 - r);
            float dx = x - cx, dy = y - cy;
            return dx * dx + dy * dy <= r * r;
        }
    }
}
