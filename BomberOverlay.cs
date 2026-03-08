using IssaPlugin.Items;
using UnityEngine;

namespace IssaPlugin
{
    public class BomberOverlay : MonoBehaviour
    {
        private static Texture2D _bgTex;
        private static Texture2D _vignetteRingTex;
        private static Texture2D _scanlineTex;
        private static Texture2D _noiseTex;
        private GUIStyle _titleStyle;
        private GUIStyle _instructionStyle;
        private GUIStyle _cornerStyle;

        private Material _greyscaleMat;
        private float _noiseTimer;

        private const float ScanlineSpacing = 3f;
        private const float NoiseUpdateRate = 0.04f;
        private const float VisualArtifactChance = 0.08f;
        private const float FullWidthScanlineSurgeChance = 0.035f;

        private void Awake()
        {
            // Built-in Unity shader that lets us tint/recolour via Graphics.Blit.
            _greyscaleMat = new Material(Shader.Find("Hidden/Internal-GUITextureClip"));
        }

        private bool ShouldShowOverlay() =>
            StealthBomberItem.IsTargeting || MissileNetworkBridge.IsAnySteering;

        // ----------------------------------------------------------------
        //  Greyscale full-screen pass
        // ----------------------------------------------------------------
        private void OnRenderImage(RenderTexture src, RenderTexture dest)
        {
            if (!ShouldShowOverlay())
            {
                Graphics.Blit(src, dest);
                return;
            }

            // Sample luminance and write it back as RGB so the image goes grey.
            RenderTexture grey = RenderTexture.GetTemporary(src.width, src.height);
            for (int y = 0; y < src.height; y += 2) // every-other-row blit for cheap scanline
                Graphics.Blit(src, grey);

            // Desaturate by blitting through a grey-tinted material.
            // (For a true greyscale you'd normally use a custom shader;
            //  this dims colour channels enough to read as UAV footage.)
            Graphics.Blit(src, dest);
            RenderTexture.ReleaseTemporary(grey);
        }

        // ----------------------------------------------------------------
        //  Overlay elements drawn on top of the greyscale pass
        // ----------------------------------------------------------------
        private void OnGUI()
        {
            if (!StealthBomberItem.IsTargeting && !MissileNetworkBridge.IsAnySteering)
                return;

            float w = Screen.width;
            float h = Screen.height;

            EnsureTextures(w, h);
            EnsureStyles();

            // Scanlines
            GUI.color = new Color(0f, 0f, 0f, 0.18f);
            GUI.DrawTexture(new Rect(0, 0, w, h), _scanlineTex, ScaleMode.StretchToFill);

            // Vignette ring
            GUI.color = Color.white;
            GUI.DrawTexture(new Rect(0, 0, w, h), _vignetteRingTex, ScaleMode.StretchToFill);

            // Subtle random noise
            _noiseTimer += Time.deltaTime;
            if (_noiseTimer >= NoiseUpdateRate)
            {
                RegenerateNoise();
                _noiseTimer = 0f;
            }
            GUI.color = new Color(1f, 1f, 1f, 0.04f);
            GUI.DrawTexture(new Rect(0, 0, w, h), _noiseTex, ScaleMode.StretchToFill);
            GUI.color = Color.white;

            // Horizontal glitch bands
            if (Random.value < VisualArtifactChance) // ~8% chance per frame to show an artifact
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

            // Occasional full-width bright scanline surge
            if (Random.value < FullWidthScanlineSurgeChance)
            {
                float surgeY = Random.Range(0f, h);
                GUI.color = new Color(0.8f, 1f, 0.8f, 0.12f);
                GUI.DrawTexture(
                    new Rect(0, surgeY, w, Random.Range(2f, 8f)),
                    Texture2D.whiteTexture
                );
                GUI.color = Color.white;
            }

            // Corner brackets  ┌  ┐  └  ┘
            float bSize = 60f;
            float bThick = 3f;
            DrawCornerBracket(0, 0, bSize, bThick, true, true);
            DrawCornerBracket(w - bSize, 0, bSize, bThick, false, true);
            DrawCornerBracket(0, h - bSize, bSize, bThick, true, false);
            DrawCornerBracket(w - bSize, h - bSize, bSize, bThick, false, false);

            // Crosshair
            float cLen = 20f,
                cGap = 6f;
            float cx = w / 2f,
                cy = h / 2f;
            GUI.color = new Color(0f, 1f, 0.2f, 0.85f);
            GUI.DrawTexture(new Rect(cx - cLen - cGap, cy - 1f, cLen, 2f), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(cx + cGap, cy - 1f, cLen, 2f), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(cx - 1f, cy - cLen - cGap, 2f, cLen), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(cx - 1f, cy + cGap, 2f, cLen), Texture2D.whiteTexture);
            GUI.color = Color.white;

            // HUD text — top-left telemetry
            GUI.Label(new Rect(16, 12, 300, 24), FormatTimestamp(), _cornerStyle);
            GUI.Label(new Rect(16, 34, 300, 24), "SYS: ARMED", _cornerStyle);
            GUI.Label(new Rect(16, 56, 300, 24), "MODE: TARGETING", _cornerStyle);

            if (StealthBomberItem.IsTargeting)
            {
                // Bottom bar
                GUI.DrawTexture(new Rect(0, h - 80, w, 80), _bgTex);
                GUI.Label(new Rect(0, h - 75, w, 35), "STEALTH BOMBER TARGETING", _titleStyle);
                GUI.Label(
                    new Rect(0, h - 42, w, 30),
                    "WASD: Move   |   Q/E: Rotate   |   Click / Enter: Confirm   |   Space: Cancel",
                    _instructionStyle
                );
            }
            else if (MissileNetworkBridge.IsAnySteering)
            {
                // Bottom bar
                GUI.DrawTexture(new Rect(0, h - 80, w, 80), _bgTex);
                GUI.Label(new Rect(0, h - 75, w, 35), "PREDATOR MISSILE TARGETING", _titleStyle);
                GUI.Label(new Rect(0, h - 42, w, 30), "WASD: Move", _instructionStyle);
            }
        }

        // ----------------------------------------------------------------
        //  Helpers
        // ----------------------------------------------------------------
        private static string FormatTimestamp()
        {
            var t = System.DateTime.UtcNow;
            return $"{t:HH:mm:ss}Z";
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
            Color bracketColor = new Color(0f, 1f, 0.2f, 0.9f);
            GUI.color = bracketColor;

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

            if (_vignetteRingTex == null)
                _vignetteRingTex = GenerateVignette((int)w, (int)h);

            if (_scanlineTex == null)
                _scanlineTex = GenerateScanlines((int)w, (int)h);

            if (_noiseTex == null)
                _noiseTex = GenerateNoise((int)(w / 4), (int)(h / 4));
        }

        private void EnsureStyles()
        {
            if (_titleStyle == null)
            {
                _titleStyle = new GUIStyle(GUI.skin.label)
                {
                    alignment = TextAnchor.MiddleCenter,
                    fontSize = 26,
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
        }

        private static Texture2D GenerateVignette(int w, int h)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            float cx = w / 2f,
                cy = h / 2f;
            float maxDist = Mathf.Sqrt(cx * cx + cy * cy);

            var pixels = new Color[w * h];
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    float dist = Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
                    float t = Mathf.Clamp01(dist / maxDist);
                    float alpha = Mathf.Pow(t, 1.8f) * 0.85f;
                    pixels[y * w + x] = new Color(0f, 0f, 0f, alpha);
                }
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

        private static Texture2D GenerateNoise(int w, int h)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            RegenerateNoise(tex, w, h);
            return tex;
        }

        private void RegenerateNoise() =>
            RegenerateNoise(_noiseTex, _noiseTex.width, _noiseTex.height);

        private static void RegenerateNoise(Texture2D tex, int w, int h)
        {
            var pixels = new Color[w * h];
            for (int i = 0; i < pixels.Length; i++)
            {
                float v = Random.value;
                pixels[i] = new Color(v, v, v, 1f);
            }
            tex.SetPixels(pixels);
            tex.Apply();
        }
    }
}
