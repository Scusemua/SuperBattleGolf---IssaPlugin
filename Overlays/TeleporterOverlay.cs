using IssaPlugin.Items;
using UnityEngine;

namespace IssaPlugin.Overlays
{
    /// <summary>
    /// Full-screen IMGUI overlay displayed while the local player is in the
    /// Teleporter's targeting phase.  Styled after <see cref="BomberOverlay"/>:
    /// scanlines, corner brackets, a crosshair, and a bottom instruction bar.
    /// </summary>
    public class TeleporterOverlay : MonoBehaviour
    {
        // ── Cached textures ───────────────────────────────────────────────────
        private static Texture2D _bgTex;
        private static Texture2D _vignetteRingTex;
        private static Texture2D _scanlineTex;
        private static Texture2D _noiseTex;

        // ── GUI styles ────────────────────────────────────────────────────────
        private GUIStyle _titleStyle;
        private GUIStyle _instructionStyle;
        private GUIStyle _cornerStyle;

        // ── Animation state ───────────────────────────────────────────────────
        private float _noiseTimer;
        private const float NoiseUpdateRate = 0.04f;
        private const float VisualArtifactChance = 0.07f;
        private const float FullWidthScanlineSurgeChance = 0.03f;

        // ── OnGUI ─────────────────────────────────────────────────────────────

        private void OnGUI()
        {
            if (!TeleporterItem.IsTargeting)
                return;

            float w = Screen.width;
            float h = Screen.height;

            EnsureTextures(w, h);
            EnsureStyles();

            // Scanlines
            GUI.color = new Color(0f, 0.4f, 1f, 0.12f);
            GUI.DrawTexture(new Rect(0, 0, w, h), _scanlineTex, ScaleMode.StretchToFill);

            // Vignette ring
            GUI.color = Color.white;
            GUI.DrawTexture(new Rect(0, 0, w, h), _vignetteRingTex, ScaleMode.StretchToFill);

            // Random noise
            _noiseTimer += Time.deltaTime;
            if (_noiseTimer >= NoiseUpdateRate)
            {
                RegenerateNoise();
                _noiseTimer = 0f;
            }
            GUI.color = new Color(1f, 1f, 1f, 0.03f);
            GUI.DrawTexture(new Rect(0, 0, w, h), _noiseTex, ScaleMode.StretchToFill);
            GUI.color = Color.white;

            // Glitch bands
            if (Random.value < VisualArtifactChance)
            {
                int bandCount = Random.Range(1, 4);
                for (int i = 0; i < bandCount; i++)
                {
                    float bandY = Random.Range(0f, h);
                    float bandH = Random.Range(1f, 5f);
                    float bandW = Random.Range(w * 0.15f, w * 0.75f);
                    float bandX = Random.Range(0f, w - bandW);
                    GUI.color = new Color(0.3f, 0.7f, 1f, Random.Range(0.04f, 0.16f));
                    GUI.DrawTexture(new Rect(bandX, bandY, bandW, bandH), Texture2D.whiteTexture);
                }
                GUI.color = Color.white;
            }

            // Occasional full-width surge
            if (Random.value < FullWidthScanlineSurgeChance)
            {
                float surgeY = Random.Range(0f, h);
                GUI.color = new Color(0.3f, 0.7f, 1f, 0.10f);
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

            // Crosshair
            float cLen = 20f,
                cGap = 6f;
            float cx = w / 2f,
                cy = h / 2f;
            GUI.color = new Color(0.2f, 0.6f, 1f, 0.9f);
            GUI.DrawTexture(new Rect(cx - cLen - cGap, cy - 1f, cLen, 2f), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(cx + cGap, cy - 1f, cLen, 2f), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(cx - 1f, cy - cLen - cGap, 2f, cLen), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(cx - 1f, cy + cGap, 2f, cLen), Texture2D.whiteTexture);
            // Centre dot
            GUI.DrawTexture(new Rect(cx - 2f, cy - 2f, 4f, 4f), Texture2D.whiteTexture);
            GUI.color = Color.white;

            // Telemetry labels — top-left
            GUI.Label(
                new Rect(16, 12, 300, 24),
                System.DateTime.UtcNow.ToString("HH:mm:ss") + "Z",
                _cornerStyle
            );
            GUI.Label(new Rect(16, 34, 300, 24), "SYS: ARMED", _cornerStyle);
            GUI.Label(new Rect(16, 56, 300, 24), "MODE: TELEPORT TARGETING", _cornerStyle);

            // Bottom instruction bar
            GUI.DrawTexture(new Rect(0, h - 80, w, 80), _bgTex);
            GUI.Label(new Rect(0, h - 75, w, 35), "TELEPORTER TARGETING", _titleStyle);
            GUI.Label(
                new Rect(0, h - 42, w, 30),
                "WASD: Move Target   |   Scroll: Zoom   |   Click / Enter: Confirm   |   Space: Cancel",
                _instructionStyle
            );
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private void DrawCornerBracket(
            float x,
            float y,
            float size,
            float thick,
            bool leftSide,
            bool topSide
        )
        {
            GUI.color = new Color(0.2f, 0.6f, 1f, 0.9f);

            float hLen = size;
            float vLen = size;

            // Horizontal arm
            float hx = leftSide ? x : x + size - hLen;
            float hy = topSide ? y : y + size - thick;
            GUI.DrawTexture(new Rect(hx, hy, hLen, thick), Texture2D.whiteTexture);

            // Vertical arm
            float vx = leftSide ? x : x + size - thick;
            float vy = topSide ? y : y + size - vLen;
            GUI.DrawTexture(new Rect(vx, vy, thick, vLen), Texture2D.whiteTexture);

            GUI.color = Color.white;
        }

        private void EnsureTextures(float w, float h)
        {
            int wi = Mathf.Max(1, (int)w);
            int hi = Mathf.Max(1, (int)h);

            if (_bgTex == null)
            {
                _bgTex = new Texture2D(1, 1);
                _bgTex.SetPixel(0, 0, new Color(0f, 0.05f, 0.15f, 0.82f));
                _bgTex.Apply();
            }

            if (_scanlineTex == null || _scanlineTex.width != wi)
            {
                _scanlineTex = new Texture2D(wi, hi);
                var pixels = new Color[wi * hi];
                for (int py = 0; py < hi; py++)
                {
                    Color lineColor =
                        (py % 3 == 0) ? new Color(0f, 0.3f, 0.8f, 0.15f) : Color.clear;
                    for (int px = 0; px < wi; px++)
                        pixels[py * wi + px] = lineColor;
                }
                _scanlineTex.SetPixels(pixels);
                _scanlineTex.Apply();
            }

            if (_vignetteRingTex == null || _vignetteRingTex.width != wi)
            {
                _vignetteRingTex = new Texture2D(wi, hi);
                var pixels = new Color[wi * hi];
                float cx = wi * 0.5f,
                    cy = hi * 0.5f;
                float maxR = Mathf.Sqrt(cx * cx + cy * cy);

                for (int py = 0; py < hi; py++)
                for (int px = 0; px < wi; px++)
                {
                    float dx = px - cx,
                        dy = py - cy;
                    float r = Mathf.Sqrt(dx * dx + dy * dy) / maxR;
                    float alpha = Mathf.Clamp01((r - 0.55f) / 0.45f) * 0.7f;
                    pixels[py * wi + px] = new Color(0f, 0f, 0.05f, alpha);
                }

                _vignetteRingTex.SetPixels(pixels);
                _vignetteRingTex.Apply();
            }

            if (_noiseTex == null)
                _noiseTex = new Texture2D(256, 256);
        }

        private void RegenerateNoise()
        {
            if (_noiseTex == null)
                return;

            int nw = _noiseTex.width,
                nh = _noiseTex.height;
            var pixels = new Color[nw * nh];
            for (int i = 0; i < pixels.Length; i++)
            {
                float v = Random.value;
                pixels[i] = new Color(v, v, v, Random.Range(0.01f, 0.06f));
            }
            _noiseTex.SetPixels(pixels);
            _noiseTex.Apply();
        }

        private void EnsureStyles()
        {
            if (_titleStyle != null)
                return;

            _titleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 18,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = new Color(0.4f, 0.8f, 1f) },
            };

            _instructionStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 13,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = new Color(0.7f, 0.9f, 1f) },
            };

            _cornerStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 11,
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(0.3f, 0.7f, 1f, 0.85f) },
            };
        }
    }
}
