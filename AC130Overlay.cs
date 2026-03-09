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

        private float _noiseTimer;
        private Color32[] _noisePixels;

        private static Vector3 _crosshairWorld;
        private static float _elapsed;
        private static float _duration;

        private const float ScanlineSpacing = 3f;
        private const float NoiseUpdateRate = 0.1f;

        public static void UpdateAimInfo(Vector3 crosshairWorld, float elapsed, float duration)
        {
            _crosshairWorld = crosshairWorld;
            _elapsed = elapsed;
            _duration = duration;
        }

        private void OnGUI()
        {
            if (!AC130Item.IsActive)
                return;

            float w = Screen.width;
            float h = Screen.height;

            EnsureTextures(w, h);
            EnsureStyles();

            // Scanlines
            GUI.color = new Color(0f, 0f, 0f, 0.18f);
            GUI.DrawTexture(new Rect(0, 0, w, h), _scanlineTex, ScaleMode.StretchToFill);

            // Vignette
            GUI.color = Color.white;
            GUI.DrawTexture(new Rect(0, 0, w, h), _vignetteRingTex, ScaleMode.StretchToFill);

            // Noise
            _noiseTimer += Time.deltaTime;
            if (_noiseTimer >= NoiseUpdateRate)
            {
                RegenerateNoise();
                _noiseTimer = 0f;
            }
            GUI.color = new Color(1f, 1f, 1f, 0.04f);
            GUI.DrawTexture(new Rect(0, 0, w, h), _noiseTex, ScaleMode.StretchToFill);
            GUI.color = Color.white;

            // Glitch bands
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

            // Corner brackets
            float bSize = 60f,
                bThick = 3f;
            DrawCornerBracket(0, 0, bSize, bThick, true, true);
            DrawCornerBracket(w - bSize, 0, bSize, bThick, false, true);
            DrawCornerBracket(0, h - bSize, bSize, bThick, true, false);
            DrawCornerBracket(w - bSize, h - bSize, bSize, bThick, false, false);

            // Crosshair follows the mouse cursor
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
            // Circle approximated with four arc-corners.
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

            // Top-left telemetry
            float timeRemaining = Mathf.Max(0f, _duration - _elapsed);
            GUI.Label(
                new Rect(16, 12, 300, 24),
                $"{System.DateTime.UtcNow:HH:mm:ss}Z",
                _cornerStyle
            );
            GUI.Label(new Rect(16, 34, 300, 24), "SYS: ARMED", _cornerStyle);
            GUI.Label(new Rect(16, 56, 300, 24), "MODE: GUNSHIP", _cornerStyle);
            GUI.Label(
                new Rect(16, 78, 400, 24),
                $"TGT: ({_crosshairWorld.x:F0}, {_crosshairWorld.z:F0})",
                _cornerStyle
            );

            // Timer — top right, turns red in the last 10 seconds.
            bool lowTime = timeRemaining <= 10f;
            _timerStyle.normal.textColor = lowTime
                ? new Color(1f, 0.2f, 0.2f)
                : new Color(0f, 1f, 0.2f, 0.9f);
            GUI.Label(new Rect(w - 220f, 12, 200, 30), $"TIME: {timeRemaining:F1}s", _timerStyle);

            // Bottom bar
            if (_bgTex != null)
                GUI.DrawTexture(new Rect(0, h - 80, w, 80), _bgTex);

            GUI.Label(new Rect(0, h - 75, w, 35), "AC-130 GUNSHIP", _titleStyle);
            GUI.Label(
                new Rect(0, h - 42, w, 30),
                "Aim: Mouse   |   Click: Shoot   |   Q/E: Raise/Lower   |   Shift: Speed Up   |   Space: Exit",
                _instructionStyle
            );
        }

        // ----------------------------------------------------------------
        //  Shared helpers (same as BomberOverlay)
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
                    fontSize = 28,
                    fontStyle = FontStyle.Bold,
                };
                _titleStyle.normal.textColor = new Color(1f, 0.5f, 0f);
            }

            if (_instructionStyle == null)
            {
                _instructionStyle = new GUIStyle(GUI.skin.label)
                {
                    alignment = TextAnchor.MiddleCenter,
                    fontSize = 18,
                    fontStyle = FontStyle.Bold,
                };
                _instructionStyle.normal.textColor = Color.white;
            }

            if (_cornerStyle == null)
            {
                _cornerStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 20,
                    fontStyle = FontStyle.Bold,
                };
                _cornerStyle.normal.textColor = new Color(0f, 1f, 0.2f, 0.9f);
            }

            if (_timerStyle == null)
            {
                _timerStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 24,
                    fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.MiddleRight,
                };
                _timerStyle.normal.textColor = new Color(0f, 1f, 0.2f, 0.9f);
            }
        }

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
            _noisePixels = new Color32[w * h];
            RegenerateNoise();
            return tex;
        }

        private void RegenerateNoise()
        {
            for (int i = 0; i < _noisePixels.Length; i++)
            {
                byte v = (byte)Random.Range(0, 256);
                _noisePixels[i] = new Color32(v, v, v, 255);
            }

            if (_noiseTex != null)
            {
                _noiseTex.SetPixels32(_noisePixels);
                _noiseTex.Apply(false, false);
            }
        }
    }
}
