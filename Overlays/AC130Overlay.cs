using IssaPlugin.Items;
using UnityEngine;

namespace IssaPlugin
{
    public class AC130Overlay : MonoBehaviour
    {
        private static Texture2D _bgTex;
        private static Texture2D _vignetteRingTex;
        private static Texture2D _scanlineTex;
        private static Texture2D _noiseTex;

        private GUIStyle _titleStyle;
        private GUIStyle _instructionStyle;
        private GUIStyle _cornerStyle;
        private GUIStyle _timerStyle;
        private GUIStyle _maydayStyle;

        private float _noiseTimer;

        // UV offset into the static noise texture, reshuffled on the noise cadence.
        private Vector2 _noiseOffset;
        private Color32[] _noisePixels;

        private static Vector3 _crosshairWorld;
        private static float _elapsed;
        private static float _duration;
        private static bool _maydayActive;

        // Heavy rocket HUD state
        private static bool _heavyMode;
        private static int _heavyShotsLeft;
        private static int _heavyMaxShots;
        private static bool _heavyReloading;
        private static float _heavyReloadProgress; // 0 = just started, 1 = done

        private GUIStyle _modeStyle;
        private GUIStyle _heavyAmmoStyle;

        // Glass crack procedural overlay — generated once, reused during mayday.
        private static Texture2D _glassCrackTex;
        private static bool _glassCrackGenerated;

        private const float ScanlineSpacing = 3f;
        private const float NoiseUpdateRate = 0.1f;

        // Lazily cached reference to the local player's bridge.
        private AC130NetworkBridge _localBridge;
        private AC130NetworkBridge LocalBridge
        {
            get
            {
                if (_localBridge != null)
                    return _localBridge;
                var movement = GameManager.LocalPlayerMovement;
                if (movement != null)
                    _localBridge = movement.GetComponent<AC130NetworkBridge>();
                return _localBridge;
            }
        }

        public static void UpdateAimInfo(Vector3 crosshairWorld, float elapsed, float duration)
        {
            _crosshairWorld = crosshairWorld;
            _elapsed = elapsed;
            _duration = duration;
        }

        /// <summary>Called by AC130NetworkBridge when mayday starts or ends.</summary>
        public static void SetMaydayActive(bool active)
        {
            _maydayActive = active;
            if (active && !_glassCrackGenerated)
                _glassCrackTex = GenerateGlassCrack(Screen.width, Screen.height);
        }

        /// <summary>Called every frame by AC130NetworkBridge during the on-station phase.</summary>
        public static void UpdateHeavyRocketInfo(
            bool heavyMode,
            int shotsLeft,
            int maxShots,
            bool reloading,
            float reloadProgress
        )
        {
            _heavyMode = heavyMode;
            _heavyShotsLeft = shotsLeft;
            _heavyMaxShots = maxShots;
            _heavyReloading = reloading;
            _heavyReloadProgress = reloadProgress;
        }

        // ----------------------------------------------------------------
        //  Main GUI
        // ----------------------------------------------------------------

        private void OnGUI()
        {
            var bridge = LocalBridge;
            bool sessionActive = bridge != null && bridge.LocalSessionActive;
            bool maydayActive = bridge != null && bridge.LocalMaydayActive;

            if (!sessionActive && !maydayActive)
                return;

            // OnGUI runs at least twice per frame (Layout and Repaint). Everything
            // below is pure drawing, which only takes effect during Repaint.
            if (Event.current.type != EventType.Repaint)
                return;

            float w = Screen.width;
            float h = Screen.height;

            EnsureTextures(w, h);
            EnsureStyles();

            // --- Scanlines ---
            GUI.color = new Color(0f, 0f, 0f, 0.18f);
            GUI.DrawTexture(new Rect(0, 0, w, h), _scanlineTex, ScaleMode.StretchToFill);

            // --- Vignette ---
            GUI.color = Color.white;
            GUI.DrawTexture(new Rect(0, 0, w, h), _vignetteRingTex, ScaleMode.StretchToFill);

            // --- Noise ---
            // Scroll a static noise texture rather than rebuilding its pixels. See
            // BomberOverlay for the rationale: regenerating a screen-sized noise
            // texture several times a second is significant per-frame CPU work and a
            // GPU upload, and offsetting the UVs looks the same on screen.
            _noiseTimer += Time.deltaTime;
            if (_noiseTimer >= NoiseUpdateRate)
            {
                _noiseOffset = new Vector2(Random.value, Random.value);
                _noiseTimer = 0f;
            }
            GUI.color = new Color(1f, 1f, 1f, 0.04f);
            GUI.DrawTextureWithTexCoords(
                new Rect(0, 0, w, h),
                _noiseTex,
                new Rect(_noiseOffset.x, _noiseOffset.y, 1f, 1f)
            );
            GUI.color = Color.white;

            // --- Glitch bands ---
            if (Random.value < 0.08f)
            {
                int bandCount = Random.Range(1, 4);
                for (int i = 0; i < bandCount; i++)
                {
                    float bandY = Random.Range(0f, h);
                    float bandH = Random.Range(1f, 5f);
                    float bandW = Random.Range(w * 0.2f, w * 0.85f);
                    float bandX = Random.Range(0f, w - bandW);
                    float intensity = Random.Range(0.04f, 0.18f);
                    GUI.color = new Color(0.9f, 1f, 0.9f, intensity);
                    GUI.DrawTexture(new Rect(bandX, bandY, bandW, bandH), Texture2D.whiteTexture);
                }
                GUI.color = Color.white;
            }

            if (Random.value < 0.03f)
            {
                float surgeY = Random.Range(0f, h);
                GUI.color = new Color(0.8f, 1f, 0.8f, 0.12f);
                GUI.DrawTexture(
                    new Rect(0, surgeY, w, Random.Range(2f, 8f)),
                    Texture2D.whiteTexture
                );
                GUI.color = Color.white;
            }

            // --- Corner brackets ---
            float bSize = 60f,
                bThick = 3f;
            DrawCornerBracket(0, 0, bSize, bThick, true, true);
            DrawCornerBracket(w - bSize, 0, bSize, bThick, false, true);
            DrawCornerBracket(0, h - bSize, bSize, bThick, true, false);
            DrawCornerBracket(w - bSize, h - bSize, bSize, bThick, false, false);

            if (maydayActive)
                DrawMaydayOverlay(w, h);
            else
                DrawGunshipOverlay(w, h);
        }

        // ----------------------------------------------------------------
        //  Gunship on-station overlay
        // ----------------------------------------------------------------

        private void DrawGunshipOverlay(float w, float h)
        {
            // Crosshair follows the mouse cursor.
            var mousePos =
                UnityEngine.InputSystem.Mouse.current?.position.ReadValue()
                ?? new Vector2(w / 2f, h / 2f);
            float cx = mousePos.x;
            float cy = h - mousePos.y; // InputSystem Y is bottom-up, GUI Y is top-down
            float cLen = 28f,
                cGap = 10f,
                cRadius = 22f;

            GUI.color = new Color(0f, 1f, 0.2f, 0.85f);
            GUI.DrawTexture(new Rect(cx - cLen - cGap, cy - 1f, cLen, 2f), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(cx + cGap, cy - 1f, cLen, 2f), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(cx - 1f, cy - cLen - cGap, 2f, cLen), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(cx - 1f, cy + cGap, 2f, cLen), Texture2D.whiteTexture);
            GUI.DrawTexture(
                new Rect(cx - cRadius, cy - 1f, cRadius * 0.6f, 2f),
                Texture2D.whiteTexture
            );
            GUI.DrawTexture(
                new Rect(cx + cRadius * 0.4f, cy - 1f, cRadius * 0.6f, 2f),
                Texture2D.whiteTexture
            );
            GUI.DrawTexture(
                new Rect(cx - 1f, cy - cRadius, 2f, cRadius * 0.6f),
                Texture2D.whiteTexture
            );
            GUI.DrawTexture(
                new Rect(cx - 1f, cy + cRadius * 0.4f, 2f, cRadius * 0.6f),
                Texture2D.whiteTexture
            );
            GUI.color = Color.white;

            // Top-left telemetry.
            float timeRemaining = Mathf.Max(0f, _duration - _elapsed);
            GUI.Label(
                new Rect(16, 12, 320, 28),
                $"{System.DateTime.UtcNow:HH:mm:ss}Z",
                _cornerStyle
            );
            GUI.Label(new Rect(16, 40, 320, 28), "SYS: ARMED", _cornerStyle);
            GUI.Label(new Rect(16, 68, 320, 28), "MODE: GUNSHIP", _cornerStyle);
            GUI.Label(
                new Rect(16, 96, 420, 28),
                $"TGT: ({_crosshairWorld.x:F0}, {_crosshairWorld.z:F0})",
                _cornerStyle
            );

            // Timer — top right, turns red in last 10 seconds.
            bool lowTime = timeRemaining <= 10f;
            _timerStyle.normal.textColor = lowTime
                ? new Color(1f, 0.2f, 0.2f)
                : new Color(0f, 1f, 0.2f, 0.9f);
            GUI.Label(new Rect(w - 240f, 12, 220, 34), $"TIME: {timeRemaining:F1}s", _timerStyle);

            // Mode indicator — top right, below timer.
            string modeLabel = _heavyMode ? "MODE: BIG SHOT" : "MODE: REGULAR";
            _modeStyle.normal.textColor = _heavyMode
                ? new Color(1f, 0.45f, 0f, 1f)
                : new Color(0f, 1f, 0.2f, 0.9f);
            GUI.Label(new Rect(w - 240f, 48, 220, 30), modeLabel, _modeStyle);

            // Big shot ammo panel — top right, below mode label.
            if (_heavyMode)
            {
                if (_heavyReloading)
                {
                    // Reload progress bar.
                    GUI.Label(new Rect(w - 240f, 80, 220, 26), "RELOADING", _heavyAmmoStyle);
                    float barW = 200f;
                    float barH = 8f;
                    float barX = w - 230f;
                    float barY = 108f;
                    // Track background.
                    GUI.color = new Color(1f, 1f, 1f, 0.2f);
                    GUI.DrawTexture(new Rect(barX, barY, barW, barH), Texture2D.whiteTexture);
                    // Fill.
                    GUI.color = new Color(1f, 0.45f, 0f, 0.85f);
                    GUI.DrawTexture(
                        new Rect(barX, barY, barW * _heavyReloadProgress, barH),
                        Texture2D.whiteTexture
                    );
                    GUI.color = Color.white;
                }
                else
                {
                    // Shot pips — filled orange squares for remaining, dark for spent.
                    float pipSize = 14f;
                    float pipGap = 5f;
                    float totalW = _heavyMaxShots * (pipSize + pipGap) - pipGap;
                    float startX = w - 230f + (200f - totalW) / 2f;
                    float pipY = 84f;
                    for (int i = 0; i < _heavyMaxShots; i++)
                    {
                        float px = startX + i * (pipSize + pipGap);
                        GUI.color =
                            i < _heavyShotsLeft
                                ? new Color(1f, 0.45f, 0f, 0.9f)
                                : new Color(0.3f, 0.3f, 0.3f, 0.5f);
                        GUI.DrawTexture(
                            new Rect(px, pipY, pipSize, pipSize),
                            Texture2D.whiteTexture
                        );
                    }
                    GUI.color = Color.white;
                }
            }

            // Bottom bar.
            if (_bgTex != null)
                GUI.DrawTexture(new Rect(0, h - 92, w, 92), _bgTex);
            GUI.Label(new Rect(0, h - 90, w, 42), "AC-130 GUNSHIP", _titleStyle);
            GUI.Label(
                new Rect(0, h - 48, w, 38),
                "1: Regular   2: Big Shot   |   Aim: Mouse   |   Click: Shoot   |   Q/E: Raise/Lower   |   Shift: Speed Up   |   Space: Exit",
                _instructionStyle
            );
        }

        // ----------------------------------------------------------------
        //  Mayday overlay  (drawn on top of the base overlay)
        // ----------------------------------------------------------------

        private void DrawMaydayOverlay(float w, float h)
        {
            // Red tint that increases over time using noiseTimer as a proxy.
            float redAlpha = Mathf.Clamp01(0.15f + _noiseTimer * 0.02f);
            GUI.color = new Color(0.8f, 0f, 0f, redAlpha);
            GUI.DrawTexture(new Rect(0, 0, w, h), Texture2D.whiteTexture);
            GUI.color = Color.white;

            // Procedural glass crack overlay.
            if (_glassCrackTex != null)
            {
                GUI.color = new Color(1f, 1f, 1f, 0.55f);
                GUI.DrawTexture(new Rect(0, 0, w, h), _glassCrackTex, ScaleMode.StretchToFill);
                GUI.color = Color.white;
            }

            // Top-left warning text.
            GUI.Label(
                new Rect(16, 12, 420, 28),
                $"{System.DateTime.UtcNow:HH:mm:ss}Z",
                _cornerStyle
            );
            GUI.Label(new Rect(16, 40, 420, 28), "SYS: CRITICAL FAILURE", _cornerStyle);
            GUI.Label(new Rect(16, 68, 420, 28), "MODE: MAYDAY", _cornerStyle);

            // Flashing MAYDAY banner.
            if ((int)(Time.unscaledTime * 4f) % 2 == 0)
            {
                GUI.Label(
                    new Rect(0, h * 0.3f, w, 70),
                    "⚠  MAYDAY  MAYDAY  MAYDAY  ⚠",
                    _maydayStyle
                );
            }

            // Bottom instructions.
            if (_bgTex != null)
                GUI.DrawTexture(new Rect(0, h - 92, w, 92), _bgTex);
            GUI.Label(new Rect(0, h - 90, w, 42), "GOING DOWN", _titleStyle);
            GUI.Label(
                new Rect(0, h - 48, w, 38),
                "W/S: Pull Up/Down   |   A/D: Roll",
                _instructionStyle
            );
        }

        // ----------------------------------------------------------------
        //  Shared helpers
        // ----------------------------------------------------------------

        private void DrawCornerBracket(
            float x,
            float y,
            float size,
            float thick,
            bool leftSide,
            bool topSide
        )
        {
            GUI.color = new Color(0f, 1f, 0.2f, 0.9f);
            float hx = leftSide ? x : x + size - thick;
            GUI.DrawTexture(new Rect(hx, y, thick, size), Texture2D.whiteTexture);
            float hy = topSide ? y : y + size - thick;
            GUI.DrawTexture(new Rect(x, hy, size, thick), Texture2D.whiteTexture);
            GUI.color = Color.white;
        }

        private void EnsureTextures(float w, float h)
        {
            if (_bgTex == null)
            {
                _bgTex = new Texture2D(1, 1);
                _bgTex.SetPixel(0, 0, new Color(0f, 0f, 0f, 0.6f));
                _bgTex.Apply();
            }

            int qw = Mathf.Max(1, (int)w / 4);
            int qh = Mathf.Max(1, (int)h / 4);

            if (_vignetteRingTex == null)
                _vignetteRingTex = GenerateVignette(qw, qh);

            if (_scanlineTex == null)
                _scanlineTex = GenerateScanlines(qw, qh);

            int nw = Mathf.Max(1, (int)w / 8);
            int nh = Mathf.Max(1, (int)h / 8);

            if (_noiseTex == null)
                _noiseTex = GenerateNoise(nw, nh);
        }

        private void EnsureStyles()
        {
            if (_titleStyle == null)
            {
                _titleStyle = new GUIStyle(GUI.skin.label)
                {
                    alignment = TextAnchor.MiddleCenter,
                    fontSize = 32,
                    fontStyle = FontStyle.Bold,
                };
                _titleStyle.normal.textColor = new Color(1f, 0.5f, 0f);
            }

            if (_instructionStyle == null)
            {
                _instructionStyle = new GUIStyle(GUI.skin.label)
                {
                    alignment = TextAnchor.MiddleCenter,
                    fontSize = 22,
                    fontStyle = FontStyle.Bold,
                };
                _instructionStyle.normal.textColor = Color.white;
            }

            if (_cornerStyle == null)
            {
                _cornerStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 24,
                    fontStyle = FontStyle.Bold,
                };
                _cornerStyle.normal.textColor = new Color(0f, 1f, 0.2f, 0.9f);
            }

            if (_timerStyle == null)
            {
                _timerStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 28,
                    fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.MiddleRight,
                };
                _timerStyle.normal.textColor = new Color(0f, 1f, 0.2f, 0.9f);
            }

            if (_maydayStyle == null)
            {
                _maydayStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 42,
                    fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.MiddleCenter,
                };
                _maydayStyle.normal.textColor = new Color(1f, 0.1f, 0.1f, 1f);
            }

            if (_modeStyle == null)
            {
                _modeStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 22,
                    fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.MiddleRight,
                };
                _modeStyle.normal.textColor = new Color(0f, 1f, 0.2f, 0.9f);
            }

            if (_heavyAmmoStyle == null)
            {
                _heavyAmmoStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 20,
                    fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.MiddleRight,
                };
                _heavyAmmoStyle.normal.textColor = new Color(1f, 0.45f, 0f, 1f);
            }
        }

        // ----------------------------------------------------------------
        //  Texture generators
        // ----------------------------------------------------------------

        private static Texture2D GenerateVignette(int w, int h)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            float cx = w / 2f,
                cy = h / 2f;
            float maxDist = Mathf.Sqrt(cx * cx + cy * cy);
            var pixels = new Color[w * h];
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float dist = Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
                float t = Mathf.Clamp01(dist / maxDist);
                float alpha = Mathf.Pow(t, 1.8f) * 0.85f;
                pixels[y * w + x] = new Color(0f, 0f, 0f, alpha);
            }
            tex.SetPixels(pixels);
            tex.Apply();
            return tex;
        }

        private static Texture2D GenerateScanlines(int w, int h)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            var pixels = new Color[w * h];
            for (int y = 0; y < h; y++)
            {
                float alpha = (y % (int)ScanlineSpacing == 0) ? 1f : 0f;
                for (int x = 0; x < w; x++)
                    pixels[y * w + x] = new Color(0f, 0f, 0f, alpha);
            }
            tex.SetPixels(pixels);
            tex.Apply();
            return tex;
        }

        private Texture2D GenerateNoise(int w, int h)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            // Repeat so the drawn UV rect can be offset and still tile.
            tex.wrapMode = TextureWrapMode.Repeat;

            // Fill the texture passed in rather than going through _noiseTex, which is
            // not assigned until this method returns. The previous version called
            // RegenerateNoise() here, whose null check on _noiseTex meant the initial
            // texture was left blank until the first timer tick refilled it.
            _noisePixels = new Color32[w * h];
            for (int i = 0; i < _noisePixels.Length; i++)
            {
                byte v = (byte)Random.Range(0, 256);
                _noisePixels[i] = new Color32(v, v, v, 255);
            }
            tex.SetPixels32(_noisePixels);
            tex.Apply(false, false);
            return tex;
        }

        // ----------------------------------------------------------------
        //  Glass crack procedural generator
        // ----------------------------------------------------------------

        private static Texture2D GenerateGlassCrack(int w, int h)
        {
            _glassCrackGenerated = true;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);

            var pixels = new Color32[w * h];
            for (int i = 0; i < pixels.Length; i++)
                pixels[i] = new Color32(200, 200, 255, 0);

            // 3-5 main branches from a random cockpit-centre impact point.
            int cx = Random.Range(w / 3, 2 * w / 3);
            int cy = Random.Range(h / 3, 2 * h / 3);
            int branches = Random.Range(3, 6);

            // Length must be long enough to reach any screen corner from the impact point.
            // The furthest corner is at most the full diagonal away.
            int diagonal = Mathf.CeilToInt(Mathf.Sqrt(w * w + h * h));
            int minLen = diagonal / 2;
            int maxLen = diagonal;

            for (int b = 0; b < branches; b++)
            {
                float angle = (360f / branches) * b + Random.Range(-25f, 25f);
                DrawCrackLine(pixels, w, h, cx, cy, angle, Random.Range(minLen, maxLen), 2);
            }

            tex.SetPixels32(pixels);
            tex.Apply();
            return tex;
        }

        private static void DrawCrackLine(
            Color32[] pixels,
            int w,
            int h,
            int x,
            int y,
            float angle,
            int length,
            int depth
        )
        {
            if (depth <= 0 || length <= 0)
                return;

            float fx = x,
                fy = y;
            float stepX = Mathf.Cos(angle * Mathf.Deg2Rad);
            float stepY = Mathf.Sin(angle * Mathf.Deg2Rad);

            for (int i = 0; i < length; i++)
            {
                int px = Mathf.RoundToInt(fx);
                int py = Mathf.RoundToInt(fy);

                if (px < 0 || px >= w || py < 0 || py >= h)
                    break;

                pixels[py * w + px] = new Color32(200, 210, 255, 220);

                fx += stepX;
                fy += stepY;

                // Random sub-branch — probability scaled so each crack spawns
                // roughly 2-3 sub-branches total regardless of its length.
                if (Random.value < 3f / length)
                {
                    float branchAngle = angle + Random.Range(-50f, 50f);
                    DrawCrackLine(
                        pixels,
                        w,
                        h,
                        px,
                        py,
                        branchAngle,
                        (int)(length * Random.Range(0.3f, 0.5f)),
                        depth - 1
                    );
                }

                // Slight drift — kept small so long cracks don't spiral back to center.
                angle += Random.Range(-1f, 1f);
                stepX = Mathf.Cos(angle * Mathf.Deg2Rad);
                stepY = Mathf.Sin(angle * Mathf.Deg2Rad);
            }
        }
    }
}
