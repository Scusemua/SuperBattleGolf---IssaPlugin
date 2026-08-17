using UnityEngine;

namespace IssaPlugin.Overlays
{
    /// Static HUD overlay displayed during an active Donut session.
    /// Shows remaining laser uses and session time remaining.
    /// Activated/deactivated by DonutNetworkBridge.RunLocalSession.
    public static class DonutOverlay
    {
        private static GameObject _go;
        private static DonutOverlayRenderer _renderer;

        public static void SetActive(bool active, int totalLaserUses)
        {
            if (active)
            {
                if (_go == null)
                {
                    _go = new GameObject("DonutOverlay");
                    Object.DontDestroyOnLoad(_go);
                    _renderer = _go.AddComponent<DonutOverlayRenderer>();
                }

                _renderer.TotalLaserUses = totalLaserUses;
                _renderer.RemainingLaserUses = totalLaserUses;
                _renderer.TimeRemaining = ModConfig.Donut.Duration.Value;
                _go.SetActive(true);
            }
            else
            {
                if (_go != null)
                    _go.SetActive(false);
            }
        }

        public static void UpdateLaserUses(int remaining)
        {
            if (_renderer != null)
                _renderer.RemainingLaserUses = remaining;
        }

        public static void UpdateTimeRemaining(float seconds)
        {
            if (_renderer != null)
                _renderer.TimeRemaining = seconds;
        }

        /// <summary>
        /// Call this immediately when the laser fires to trigger a brief screen flash.
        /// </summary>
        public static void TriggerLaserFlash()
        {
            if (_renderer != null)
                _renderer.TriggerFlash();
        }
    }

    internal class DonutOverlayRenderer : MonoBehaviour
    {
        public int TotalLaserUses;
        public int RemainingLaserUses;
        public float TimeRemaining;

        // ── Styles ────────────────────────────────────────────────────────
        private GUIStyle _labelStyle;
        private GUIStyle _titleStyle;
        private GUIStyle _instructionStyle;
        private GUIStyle _dotStyle;
        private GUIStyle _cornerStyle;
        private GUIStyle _chargeStyle;

        // ── Textures ──────────────────────────────────────────────────────
        private Texture2D _bgTex;
        private Texture2D _vignetteTex;
        private Texture2D _noiseTex;
        private Texture2D _scanlineTex;

        // ── Flash ─────────────────────────────────────────────────────────
        private float _flashAlpha;
        private const float FlashDecayRate = 4f;

        // ── Noise ─────────────────────────────────────────────────────────
        // _noisePixels is allocated ONCE in Awake and reused on every update.
        // Color32 (4 bytes each) vs Color (16 bytes each) cuts the buffer 4x
        // and avoids float-to-byte conversion overhead inside Apply.
        private float _noiseTimer;

        // UV offset into the static noise texture, reshuffled on the noise cadence.
        private Vector2 _noiseOffset;
        private Color32[] _noisePixels;
        private const float NoiseUpdateRate = 0.05f;

        /// Rows per scanline cycle: one opaque row followed by two transparent ones.
        /// Also the height of the tiled scanline texture.
        private const int ScanlinePeriod = 3;

        // ── Recharge state ────────────────────────────────────────────────
        private int _prevRemainingUses = -1;
        private float _rechargeFlashTimer;
        private readonly float RechargeFlashDuration = ModConfig.Donut.LaserCooldown.Value;

        // ── Values cached in Update, consumed in OnGUI ────────────────────
        // OnGUI is called multiple times per frame (Layout pass + Repaint pass).
        // Reading Time.deltaTime or Screen.width inside OnGUI double-counts time
        // and re-crosses the native bridge on every pass. Cache them in Update.
        private float _sw,
            _sh;
        private float _time;
        private float _deltaTime;

        // ── Timestamp string — rebuilt at most once per second ────────────
        private string _timestampStr = "";
        private int _lastTimestampSecond = -1;

        // ── Colour constants (hot-pink / magenta palette) ─────────────────
        private static readonly Color PinkMain = new Color(1f, 0.18f, 0.72f, 1f);
        private static readonly Color PinkDim = new Color(1f, 0.18f, 0.72f, 0.55f);
        private static readonly Color PinkBright = new Color(1f, 0.55f, 0.92f, 1f);
        private static readonly Color Orange = new Color(1f, 0.55f, 0f, 1f);

        // Cached to avoid allocating a new Color struct every frame in the timer block.
        private static readonly Color TimerLowColor = new Color(1f, 0.2f, 0.2f, 1f);

        // ── Flavour strings cycling in the top-left corner ────────────────
        private static readonly string[] FlavourLines =
        {
            "GLAZING: ACTIVE",
            "SPRINKLES: ARMED",
            "CALORIES: INFINITE",
            "SUGAR RUSH: CRITICAL",
            "ICING: HOT PINK",
            "PASTRY DRIVE: ONLINE",
        };

        // ----------------------------------------------------------------
        //  Public API
        // ----------------------------------------------------------------
        public void TriggerFlash() => _flashAlpha = 1f;

        // ----------------------------------------------------------------
        //  Unity lifecycle
        // ----------------------------------------------------------------
        private void Awake()
        {
            // Pre-allocate the noise pixel buffer once — RegenerateNoise writes
            // into this same array forever with no further GC allocations.
            int nw = Mathf.Max(1, Screen.width / 4);
            int nh = Mathf.Max(1, Screen.height / 4);
            _noisePixels = new Color32[nw * nh];
        }

        // All timer / state logic lives here, not in OnGUI, because Update runs
        // exactly once per frame while OnGUI runs twice (Layout + Repaint).
        private void Update()
        {
            _sw = Screen.width;
            _sh = Screen.height;
            _time = Time.time;
            _deltaTime = Time.deltaTime;

            // Flash decay
            if (_flashAlpha > 0f)
                _flashAlpha -= _deltaTime * FlashDecayRate;

            // Noise update — offset the UVs into a static texture rather than
            // rebuilding its pixels. See BomberOverlay for the rationale.
            _noiseTimer += _deltaTime;
            if (_noiseTimer >= NoiseUpdateRate)
            {
                _noiseOffset = new Vector2(Random.value, Random.value);
                _noiseTimer = 0f;
            }

            // Laser-use spend detection
            if (_prevRemainingUses > 0 && RemainingLaserUses < _prevRemainingUses)
                _rechargeFlashTimer = RechargeFlashDuration;
            _prevRemainingUses = RemainingLaserUses;
            if (_rechargeFlashTimer > 0f)
                _rechargeFlashTimer -= _deltaTime;

            // Timestamp — only allocate a new string when the second changes
            var now = System.DateTime.UtcNow;
            if (now.Second != _lastTimestampSecond)
            {
                _timestampStr = $"{now:HH:mm:ss}Z";
                _lastTimestampSecond = now.Second;
            }
        }

        private void OnGUI()
        {
            // Checked before EnsureTextures so a disabled overlay never generates the
            // screen-sized vignette texture either.
            if (!ModConfig.Donut.OverlayEnabled.Value)
                return;

            // OnGUI runs at least twice per frame (Layout and Repaint). Everything
            // below is pure drawing, which only takes effect during Repaint.
            if (Event.current.type != EventType.Repaint)
                return;

            EnsureTextures();
            EnsureStyles();

            // Guard: _sw/_sh are 0 on the very first frame before Update runs.
            float sw = _sw > 0 ? _sw : Screen.width;
            float sh = _sh > 0 ? _sh : Screen.height;

            // ── Vignette (pink-tinted) ────────────────────────────────────
            GUI.color = Color.white;
            GUI.DrawTexture(new Rect(0, 0, sw, sh), _vignetteTex, ScaleMode.StretchToFill);

            // ── Subtle noise layer ────────────────────────────────────────
            GUI.color = new Color(1f, 0.3f, 0.8f, 0.035f);
            GUI.DrawTextureWithTexCoords(
                new Rect(0, 0, sw, sh),
                _noiseTex,
                new Rect(_noiseOffset.x, _noiseOffset.y, 1f, 1f)
            );

            // ── Scanlines ─────────────────────────────────────────────────
            // Tiled from a 1x3 strip: the UV rect repeats once per ScanlinePeriod
            // screen pixels, so one opaque row lands every third row exactly as the
            // old screen-sized texture did.
            GUI.color = new Color(0f, 0f, 0f, 0.12f);
            GUI.DrawTextureWithTexCoords(
                new Rect(0, 0, sw, sh),
                _scanlineTex,
                new Rect(0f, 0f, 1f, sh / ScanlinePeriod)
            );
            GUI.color = Color.white;

            // ── Corner brackets ───────────────────────────────────────────
            float bSize = 60f,
                bThick = 3f;
            DrawCornerBracket(0, 0, bSize, bThick, true, true);
            DrawCornerBracket(sw - bSize, 0, bSize, bThick, false, true);
            DrawCornerBracket(0, sh - bSize, bSize, bThick, true, false);
            DrawCornerBracket(sw - bSize, sh - bSize, bSize, bThick, false, false);

            // ── Top-left telemetry ────────────────────────────────────────
            int flavourIndex = (int)(_time / 2.5f) % FlavourLines.Length;
            GUI.Label(new Rect(16, 12, 360, 24), _timestampStr, _cornerStyle);
            GUI.Label(new Rect(16, 34, 360, 24), "SYS: GLAZED", _cornerStyle);
            GUI.Label(new Rect(16, 56, 360, 24), "MODE: ANNIHILATION", _cornerStyle);
            GUI.Label(new Rect(16, 78, 360, 24), FlavourLines[flavourIndex], _cornerStyle);

            // ── Top-right timer ───────────────────────────────────────────
            int secs = Mathf.CeilToInt(Mathf.Max(0f, TimeRemaining));
            bool lowTime = secs <= 10;
            _labelStyle.normal.textColor = lowTime ? TimerLowColor : PinkBright;
            _labelStyle.alignment = TextAnchor.MiddleRight;
            _labelStyle.fontSize = 26;
            GUI.Label(
                new Rect(sw - 240f, 12f, 220f, 32f),
                $"TIME  {secs / 60:D2}:{secs % 60:D2}",
                _labelStyle
            );
            // Restore shared label style defaults
            _labelStyle.alignment = TextAnchor.MiddleCenter;
            _labelStyle.fontSize = 22;
            _labelStyle.normal.textColor = Color.white;

            // ── Laser-charge bar ──────────────────────────────────────────
            DrawChargeBar(sw, sh);

            // ── Dot indicators ────────────────────────────────────────────
            DrawLaserDots(sw, sh);

            // ── Bottom bar ────────────────────────────────────────────────
            GUI.DrawTexture(new Rect(0, sh - 56f, sw, 56f), _bgTex);
            GUI.Label(new Rect(0, sh - 55f, sw, 30f), "DONUT OF ANNIHILATION", _titleStyle);
            GUI.Label(
                new Rect(0, sh - 28f, sw, 24f),
                "Click: Fire Laser   |   Space: Exit",
                _instructionStyle
            );

            // ── Laser-fire screen flash ───────────────────────────────────
            if (_flashAlpha > 0f)
            {
                GUI.color = new Color(1f, 0.2f, 0.8f, Mathf.Clamp01(_flashAlpha) * 0.55f);
                GUI.DrawTexture(new Rect(0, 0, sw, sh), Texture2D.whiteTexture);
                GUI.color = Color.white;
            }
        }

        // ----------------------------------------------------------------
        //  Draw helpers
        // ----------------------------------------------------------------

        /// Two overlapping squares that imply a short diagonal dash.
        /// Replaces the old per-pixel DrawLine loop entirely.
        private static void DrawDiagTick(
            float x,
            float y,
            float dx,
            float dy,
            float len,
            float thick
        )
        {
            GUI.DrawTexture(
                new Rect(x - thick, y - thick, thick * 2f, thick * 2f),
                Texture2D.whiteTexture
            );
            GUI.DrawTexture(
                new Rect(
                    x + dx * len * 0.5f - thick,
                    y + dy * len * 0.5f - thick,
                    thick * 2f,
                    thick * 2f
                ),
                Texture2D.whiteTexture
            );
        }

        /// Horizontal charge bar sitting just above the bottom bar.
        private void DrawChargeBar(float sw, float sh)
        {
            bool laserReady = RemainingLaserUses > 0;
            bool recharging = _rechargeFlashTimer > 0f;

            float barW = sw * 0.35f;
            float barH = 8f;
            float barX = (sw - barW) * 0.5f;
            float barY = sh - 75f;

            // Background track
            GUI.color = new Color(0f, 0f, 0f, 0.55f);
            GUI.DrawTexture(
                new Rect(barX - 2f, barY - 2f, barW + 4f, barH + 4f),
                Texture2D.whiteTexture
            );

            if (recharging)
            {
                float t = 1f - (_rechargeFlashTimer / RechargeFlashDuration);
                GUI.color = new Color(Orange.r, Orange.g, Orange.b, 0.85f);
                GUI.DrawTexture(
                    new Rect(barX, barY, barW * (1f - t), barH),
                    Texture2D.whiteTexture
                );

                _chargeStyle.normal.textColor = Orange;
                GUI.Label(new Rect(0, barY - 26f, sw, 24f), "RECHARGING...", _chargeStyle);
            }
            else if (laserReady)
            {
                float pulse = 0.75f + 0.25f * Mathf.Sin(_time * 6f);
                GUI.color = new Color(PinkMain.r, PinkMain.g, PinkMain.b, pulse);
                GUI.DrawTexture(new Rect(barX, barY, barW, barH), Texture2D.whiteTexture);

                float labelAlpha = 0.7f + 0.3f * Mathf.Sin(_time * 5f);
                _chargeStyle.normal.textColor = new Color(
                    PinkBright.r,
                    PinkBright.g,
                    PinkBright.b,
                    labelAlpha
                );
                GUI.Label(new Rect(0, barY - 26f, sw, 24f), "◆ LASER READY ◆", _chargeStyle);
            }
            else
            {
                GUI.color = new Color(0.3f, 0.3f, 0.3f, 0.5f);
                GUI.DrawTexture(new Rect(barX, barY, barW, barH), Texture2D.whiteTexture);

                _chargeStyle.normal.textColor = new Color(0.6f, 0.6f, 0.6f, 0.9f);
                GUI.Label(new Rect(0, barY - 26f, sw, 24f), "NO CHARGES", _chargeStyle);
            }

            GUI.color = Color.white;
        }

        /// Dot indicators with rainbow hue cycling on available dots.
        private void DrawLaserDots(float sw, float sh)
        {
            float dotSize = 60f;
            float gap = 10f;
            float totalW = TotalLaserUses * dotSize + (TotalLaserUses - 1) * gap;
            float startX = sw * 0.5f - totalW * 0.5f;
            float dotY = sh - 160f;

            for (int i = 0; i < TotalLaserUses; i++)
            {
                if (i < RemainingLaserUses)
                {
                    float hue = (_time * 0.4f + i * (1f / TotalLaserUses)) % 1f;
                    _dotStyle.normal.textColor = Color.HSVToRGB(hue, 0.7f, 1f);
                }
                else
                {
                    _dotStyle.normal.textColor = new Color(0.25f, 0.25f, 0.25f);
                }

                GUI.Label(
                    new Rect(startX + i * (dotSize + gap), dotY, dotSize, dotSize),
                    "●",
                    _dotStyle
                );
            }

            _labelStyle.fontSize = 13;
            _labelStyle.normal.textColor = PinkDim;
            // GUI.Label(
            //     new Rect(sw * 0.5f - 40f, dotY + dotSize + 2f, 80f, 20f),
            //     "LASER",
            //     _labelStyle
            // );
            _labelStyle.fontSize = 22;
            _labelStyle.normal.textColor = Color.white;
        }

        private void DrawCornerBracket(
            float x,
            float y,
            float size,
            float thick,
            bool leftSide,
            bool topSide
        )
        {
            GUI.color = PinkMain;
            float hx = leftSide ? x : x + size - thick;
            GUI.DrawTexture(new Rect(hx, y, thick, size), Texture2D.whiteTexture);
            float hy = topSide ? y : y + size - thick;
            GUI.DrawTexture(new Rect(x, hy, size, thick), Texture2D.whiteTexture);
            GUI.color = Color.white;
        }

        // ----------------------------------------------------------------
        //  Texture generators
        // ----------------------------------------------------------------

        private void EnsureTextures()
        {
            float sw = _sw > 0 ? _sw : Screen.width;
            float sh = _sh > 0 ? _sh : Screen.height;

            if (_bgTex == null)
            {
                _bgTex = new Texture2D(1, 1);
                _bgTex.SetPixel(0, 0, new Color(0f, 0f, 0f, 0.65f));
                _bgTex.Apply();
            }

            if (_vignetteTex == null)
                _vignetteTex = GeneratePinkVignette((int)sw, (int)sh);

            if (_noiseTex == null)
                _noiseTex = GenerateNoise((int)(sw / 4), (int)(sh / 4));

            if (_scanlineTex == null)
                _scanlineTex = GenerateScanlines();
        }

        private static Texture2D GeneratePinkVignette(int w, int h)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            float cx = w / 2f,
                cy = h / 2f;
            float maxDist = Mathf.Sqrt(cx * cx + cy * cy);
            var pixels = new Color[w * h];

            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float dx = x - cx,
                    dy = y - cy;
                float t = Mathf.Clamp01(Mathf.Sqrt(dx * dx + dy * dy) / maxDist);
                float a = Mathf.Pow(t, 1.7f) * 0.90f;
                float pink = Mathf.Pow(t, 3f) * 0.35f;
                pixels[y * w + x] = new Color(pink * 0.9f, 0f, pink * 0.6f, a);
            }
            tex.SetPixels(pixels);
            tex.Apply();
            return tex;
        }

        /// Builds the scanline pattern as a 1x3 strip rather than a screen-sized
        /// texture.
        ///
        /// The pattern only varies along Y — every pixel in a row is identical — so a
        /// full-resolution texture stored the same three rows a few hundred times over.
        /// At 1440p that was a ~3.7M pixel allocation and upload, two thirds of it
        /// fully transparent. Drawn with DrawTextureWithTexCoords and a UV height of
        /// (screenHeight / 3), this tiles to a pixel-identical result.
        private static Texture2D GenerateScanlines()
        {
            var tex = new Texture2D(1, ScanlinePeriod, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Point,
            };

            var pixels = new Color32[ScanlinePeriod];
            for (int y = 0; y < ScanlinePeriod; y++)
                pixels[y] = new Color32(0, 0, 0, (byte)(y == 0 ? 255 : 0));

            tex.SetPixels32(pixels);
            tex.Apply(false);
            return tex;
        }

        private Texture2D GenerateNoise(int w, int h)
        {
            // Guard against resolution changes between Awake and first render.
            if (_noisePixels == null || _noisePixels.Length != w * h)
                _noisePixels = new Color32[w * h];

            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            // Repeat so the drawn UV rect can be offset and still tile.
            tex.wrapMode = TextureWrapMode.Repeat;
            RegenerateNoise(tex);
            return tex;
        }

        /// Fills the pre-allocated Color32 buffer with pink-tinted noise and uploads it.
        /// Called once when the texture is created; the runtime "static" effect comes
        /// from offsetting the sampled UVs instead of refilling these pixels.
        private void RegenerateNoise(Texture2D tex)
        {
            for (int i = 0; i < _noisePixels.Length; i++)
            {
                byte v = (byte)Random.Range(0, 256);
                _noisePixels[i] = new Color32(v, (byte)(v * 0.4f), (byte)(v * 0.85f), 255);
            }
            // Apply(false, false): skip mipmap regen, don't mark non-readable —
            // the two cheapest flags for a texture updated every few frames.
            tex.SetPixels32(_noisePixels);
            tex.Apply(false, false);
        }

        // ----------------------------------------------------------------
        //  Style initialisation
        // ----------------------------------------------------------------
        private void EnsureStyles()
        {
            if (_labelStyle == null)
            {
                _labelStyle = new GUIStyle
                {
                    fontSize = 22,
                    fontStyle = FontStyle.Bold,
                    normal = { textColor = Color.white },
                    alignment = TextAnchor.MiddleCenter,
                };
            }

            if (_titleStyle == null)
            {
                _titleStyle = new GUIStyle(GUI.skin.label)
                {
                    alignment = TextAnchor.MiddleCenter,
                    fontSize = 24,
                    fontStyle = FontStyle.Bold,
                };
                _titleStyle.normal.textColor = PinkBright;
            }

            if (_dotStyle == null)
            {
                _dotStyle = new GUIStyle
                {
                    fontSize = 26,
                    fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.MiddleCenter,
                };
            }

            if (_instructionStyle == null)
            {
                _instructionStyle = new GUIStyle(GUI.skin.label)
                {
                    alignment = TextAnchor.MiddleCenter,
                    fontSize = 16,
                    fontStyle = FontStyle.Bold,
                };
                _instructionStyle.normal.textColor = new Color(1f, 1f, 1f, 0.75f);
            }

            if (_cornerStyle == null)
            {
                _cornerStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 18,
                    fontStyle = FontStyle.Bold,
                };
                _cornerStyle.normal.textColor = PinkMain;
            }

            if (_chargeStyle == null)
            {
                _chargeStyle = new GUIStyle(GUI.skin.label)
                {
                    alignment = TextAnchor.MiddleCenter,
                    fontSize = 18,
                    fontStyle = FontStyle.Bold,
                };
                _chargeStyle.normal.textColor = PinkBright;
            }
        }
    }
}
