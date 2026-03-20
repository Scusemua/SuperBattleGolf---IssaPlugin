using IssaPlugin.Items;
using UnityEngine;

namespace IssaPlugin.Overlays
{
    /// <summary>
    /// Minimal HUD shown to the player who summoned the bears.
    /// Displays how many bears are still alive and a countdown until the
    /// session automatically ends, with a deliberately rough, chaotic aesthetic
    /// that fits the bear rampage theme.
    ///
    /// Unlike the AC130/Donut overlays this is intentionally lightweight:
    /// - No scanlines / vignette (the player retains normal gameplay vision)
    /// - Corner warning sigils only
    /// - Bear-count pips that disappear as bears are killed
    ///
    /// Activated via BearOverlay.SetActive(true, bearCount) and updated each
    /// frame by BearNetworkBridge or by BearOverlay.DecrementBearCount().
    ///
    /// Added to the Plugin's persistent GameObject in Plugin.cs.
    /// </summary>
    public class BearOverlay : MonoBehaviour
    {
        public static BearOverlay Instance { get; private set; }

        // ── State ──────────────────────────────────────────────────────────────
        private bool _active;
        private int _totalBears;
        private int _aliveBears;
        private float _startTime;
        private float _sessionDuration;

        // ── Textures ───────────────────────────────────────────────────────────
        private Texture2D _solidTex;

        // ── Styles ────────────────────────────────────────────────────────────
        private GUIStyle _warningStyle;
        private GUIStyle _countStyle;
        private GUIStyle _timerStyle;
        private GUIStyle _labelStyle;

        // ── Animation state ───────────────────────────────────────────────────
        // Cached in Update, consumed in OnGUI (OnGUI runs twice per frame)
        private float _time;
        private float _deltaTime;
        private float _sw,
            _sh;

        // Bear-kill flash
        private float _killFlashAlpha;
        private const float KillFlashDecay = 3.5f;

        // Colours
        private static readonly Color BearBrown = new Color(0.55f, 0.27f, 0.07f, 1f);
        private static readonly Color BearBrownDim = new Color(0.55f, 0.27f, 0.07f, 0.45f);
        private static readonly Color WarningRed = new Color(0.9f, 0.15f, 0.05f, 1f);
        private static readonly Color DeadGrey = new Color(0.25f, 0.25f, 0.25f, 0.55f);

        // ── Unity lifecycle ───────────────────────────────────────────────────

        private void Awake()
        {
            Instance = this;

            _solidTex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            _solidTex.SetPixel(0, 0, Color.white);
            _solidTex.Apply();
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
            if (_solidTex != null)
                Destroy(_solidTex);
        }

        private void Update()
        {
            _sw = Screen.width;
            _sh = Screen.height;
            _time = Time.time;
            _deltaTime = Time.deltaTime;

            if (_killFlashAlpha > 0f)
                _killFlashAlpha -= _deltaTime * KillFlashDecay;
        }

        // ── Public API ─────────────────────────────────────────────────────────

        public void SetActive(bool active, int bearCount, float sessionDuration)
        {
            _active = active;
            _totalBears = bearCount;
            _aliveBears = bearCount;
            _startTime = Time.time;
            _sessionDuration = sessionDuration;
        }

        public void DecrementBearCount()
        {
            _aliveBears = Mathf.Max(0, _aliveBears - 1);
            _killFlashAlpha = 1f;
        }

        public void Deactivate()
        {
            _active = false;
        }

        // ── Rendering ─────────────────────────────────────────────────────────

        private void OnGUI()
        {
            if (!_active)
                return;

            EnsureStyles();

            float sw = _sw > 0 ? _sw : Screen.width;
            float sh = _sh > 0 ? _sh : Screen.height;

            float elapsed = _time - _startTime;
            float remaining = Mathf.Max(0f, _sessionDuration - elapsed);
            bool lowTime = remaining <= 10f;

            // ── Bear-kill flash (red tint on bear death) ───────────────────────
            if (_killFlashAlpha > 0f)
            {
                GUI.color = new Color(
                    BearBrown.r,
                    BearBrown.g,
                    BearBrown.b,
                    Mathf.Clamp01(_killFlashAlpha) * 0.3f
                );
                GUI.DrawTexture(new Rect(0, 0, sw, sh), _solidTex);
                GUI.color = Color.white;
            }

            // ── Corner warning claws ───────────────────────────────────────────
            float pulse = 0.6f + 0.4f * Mathf.Sin(_time * 3f);
            DrawCornerWarnings(sw, sh, pulse);

            // ── Bear pip indicators (top-centre) ──────────────────────────────
            DrawBearPips(sw, sh);

            // ── Session timer (top-right) ──────────────────────────────────────
            _timerStyle.normal.textColor = lowTime
                ? new Color(
                    WarningRed.r,
                    WarningRed.g,
                    WarningRed.b,
                    0.7f + 0.3f * Mathf.Sin(_time * 8f)
                )
                : new Color(BearBrown.r, BearBrown.g, BearBrown.b, 0.85f);

            int secs = Mathf.CeilToInt(remaining);
            GUI.Label(
                new Rect(sw - 200f, 12f, 185f, 32f),
                $"⏱  {secs / 60:D2}:{secs % 60:D2}",
                _timerStyle
            );

            // ── Bottom bar ────────────────────────────────────────────────────
            DrawBottomBar(sw, sh);
        }

        // ── Draw helpers ──────────────────────────────────────────────────────

        private void DrawBearPips(float sw, float sh)
        {
            if (_totalBears <= 0)
                return;

            const float PipSize = 36f;
            const float PipGap = 8f;
            float totalW = _totalBears * PipSize + (_totalBears - 1) * PipGap;
            float startX = sw * 0.5f - totalW * 0.5f;
            float pipY = 14f;

            for (int i = 0; i < _totalBears; i++)
            {
                bool alive = i < _aliveBears;

                // Alive pips pulse gently; dead pips are dim grey
                if (alive)
                {
                    float hue = (0.055f + i * 0.015f) % 1f; // warm amber-brown hues
                    float pulse = 0.75f + 0.25f * Mathf.Sin(_time * 2.5f + i * 1.1f);
                    _countStyle.normal.textColor = Color.HSVToRGB(hue, 0.7f, pulse);
                }
                else
                {
                    _countStyle.normal.textColor = DeadGrey;
                }

                GUI.Label(
                    new Rect(startX + i * (PipSize + PipGap), pipY, PipSize, PipSize),
                    "🐻",
                    _countStyle
                );
            }

            // Label below the pips
            _labelStyle.normal.textColor = BearBrownDim;
            string label =
                _aliveBears == 0
                    ? "ALL BEARS DEFEATED"
                    : $"{_aliveBears} BEAR{(_aliveBears == 1 ? "" : "S")} ACTIVE";
            GUI.Label(new Rect(0, pipY + PipSize + 2f, sw, 20f), label, _labelStyle);
        }

        private void DrawCornerWarnings(float sw, float sh, float pulse)
        {
            const float Size = 48f;
            const float Thick = 3f;

            Color c = new Color(BearBrown.r, BearBrown.g, BearBrown.b, pulse * 0.9f);

            DrawCornerBracket(0, 0, Size, Thick, true, true, c);
            DrawCornerBracket(sw - Size, 0, Size, Thick, false, true, c);
            DrawCornerBracket(0, sh - Size, Size, Thick, true, false, c);
            DrawCornerBracket(sw - Size, sh - Size, Size, Thick, false, false, c);

            // Claw-scratch lines radiating from each corner — purely aesthetic
            GUI.color = new Color(BearBrown.r, BearBrown.g, BearBrown.b, pulse * 0.35f);
            // Top-left scratch marks
            DrawRect(4f, 12f, 28f, 2f);
            DrawRect(4f, 19f, 20f, 2f);
            DrawRect(4f, 26f, 14f, 2f);
            // Bottom-right scratch marks (mirrored)
            DrawRect(sw - 32f, sh - 14f, 28f, 2f);
            DrawRect(sw - 24f, sh - 21f, 20f, 2f);
            DrawRect(sw - 18f, sh - 28f, 14f, 2f);
            GUI.color = Color.white;
        }

        private void DrawBottomBar(float sw, float sh)
        {
            // Thin brown rule at the very bottom
            GUI.color = new Color(BearBrown.r, BearBrown.g, BearBrown.b, 0.55f);
            DrawRect(0, sh - 36f, sw, 36f);
            GUI.color = Color.white;

            _warningStyle.normal.textColor = new Color(1f, 0.9f, 0.75f, 0.9f);
            GUI.Label(new Rect(0, sh - 34f, sw, 18f), "BEAR ATTACK", _warningStyle);
            GUI.Label(
                new Rect(0, sh - 18f, sw, 16f),
                "Weapons kill bears  |  Bears hunt all players",
                _labelStyle
            );
        }

        private void DrawCornerBracket(
            float x,
            float y,
            float size,
            float thick,
            bool leftSide,
            bool topSide,
            Color c
        )
        {
            GUI.color = c;
            float hx = leftSide ? x : x + size - thick;
            DrawRect(hx, y, thick, size);
            float hy = topSide ? y : y + size - thick;
            DrawRect(x, hy, size, thick);
            GUI.color = Color.white;
        }

        private void DrawRect(float x, float y, float w, float h)
        {
            GUI.DrawTexture(new Rect(x, y, w, h), _solidTex);
        }

        // ── Style initialisation ──────────────────────────────────────────────

        private void EnsureStyles()
        {
            if (_warningStyle == null)
            {
                _warningStyle = new GUIStyle(GUI.skin.label)
                {
                    alignment = TextAnchor.MiddleCenter,
                    fontSize = 13,
                    fontStyle = FontStyle.Bold,
                };
                _warningStyle.normal.textColor = Color.white;
            }

            if (_countStyle == null)
            {
                _countStyle = new GUIStyle(GUI.skin.label)
                {
                    alignment = TextAnchor.MiddleCenter,
                    fontSize = 26,
                    fontStyle = FontStyle.Bold,
                };
                _countStyle.normal.textColor = BearBrown;
            }

            if (_timerStyle == null)
            {
                _timerStyle = new GUIStyle(GUI.skin.label)
                {
                    alignment = TextAnchor.MiddleRight,
                    fontSize = 20,
                    fontStyle = FontStyle.Bold,
                };
                _timerStyle.normal.textColor = BearBrown;
            }

            if (_labelStyle == null)
            {
                _labelStyle = new GUIStyle(GUI.skin.label)
                {
                    alignment = TextAnchor.MiddleCenter,
                    fontSize = 12,
                    fontStyle = FontStyle.Bold,
                };
                _labelStyle.normal.textColor = BearBrownDim;
            }
        }
    }
}
