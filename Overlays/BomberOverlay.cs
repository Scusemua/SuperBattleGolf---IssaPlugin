using IssaPlugin.Items;
using UnityEngine;

namespace IssaPlugin.Overlays
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

        private float _noiseTimer;

        // UV offset into the static noise texture. Reshuffled on the noise cadence to
        // fake regeneration without touching pixel data.
        private Vector2 _noiseOffset;

        private const float ScanlineSpacing = 3f;
        private const float NoiseUpdateRate = 0.04f;
        private const float VisualArtifactChance = 0.08f;
        private const float FullWidthScanlineSurgeChance = 0.035f;

        // Lazily cached reference to the local player's missile bridge.
        private MissileNetworkBridge _localMissileBridge;
        private MissileNetworkBridge LocalMissileBridge
        {
            get
            {
                if (_localMissileBridge != null)
                    return _localMissileBridge;
                var movement = GameManager.LocalPlayerMovement;
                if (movement != null)
                    _localMissileBridge = movement.GetComponent<MissileNetworkBridge>();
                return _localMissileBridge;
            }
        }

        private bool LocalIsSteering => LocalMissileBridge != null && LocalMissileBridge.IsSteering;

        private bool ShouldShowOverlay() =>
            ModConfig.Global.BomberOverlayEnabled.Value
            && (StealthBomberItem.IsTargeting || LocalIsSteering);

        // The greyscale OnRenderImage pass that used to live here has been removed.
        // This component is attached to the plugin's GameObject, which has no Camera,
        // and Unity only invokes OnRenderImage on a Camera's GameObject — so the pass
        // never ran. It had already been reduced to a plain src->dest copy that applied
        // no effect, and the material it allocated in Awake was never used by anything.

        // ----------------------------------------------------------------
        //  Overlay elements drawn on top of the greyscale pass
        // ----------------------------------------------------------------
        private void OnGUI()
        {
            if (!ShouldShowOverlay())
                return;

            // OnGUI runs at least twice per frame (Layout and Repaint). This overlay is
            // pure drawing — including three fullscreen stretched textures — and
            // GUI.DrawTexture only takes effect during Repaint, so the Layout pass was
            // doubling the fill cost for no visible result.
            if (Event.current.type != EventType.Repaint)
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

            // Subtle random noise.
            //
            // The texture is generated once and then scrolled by jumping to a random
            // UV offset on the same cadence the old code used to rebuild it. Visually
            // this reads the same — a full-screen field of static that reshuffles a few
            // times a second — but it costs one Rect instead of regenerating ~130k
            // pixels and re-uploading the texture to the GPU 25 times per second.
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
            else if (LocalIsSteering)
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
            // Generate at quarter resolution and let the GPU scale it up —
            // vignette gradients are smooth enough that 1/4 res is indistinguishable.
            int tw = w / 4,
                th = h / 4;
            var tex = new Texture2D(tw, th, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Bilinear;
            float cx = tw / 2f,
                cy = th / 2f;
            // Use squared distance to avoid a Sqrt per pixel (2M calls at 1080p).
            float maxDistSq = cx * cx + cy * cy;

            var pixels = new Color32[tw * th];
            for (int y = 0; y < th; y++)
            {
                float dy = y - cy;
                for (int x = 0; x < tw; x++)
                {
                    float dx = x - cx;
                    float tSq = Mathf.Clamp01((dx * dx + dy * dy) / maxDistSq);
                    // Approximate Pow(t, 1.8) cheaply with t*t for the inner region
                    // and a lerp blend — close enough for a vignette.
                    float t = Mathf.Sqrt(tSq);
                    float alpha = Mathf.Pow(t, 1.8f) * 0.85f;
                    pixels[y * tw + x] = new Color32(0, 0, 0, (byte)(alpha * 255f));
                }
            }
            tex.SetPixels32(pixels);
            tex.Apply(false);
            return tex;
        }

        private static Texture2D GenerateScanlines(int w, int h)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            var pixels = new Color32[w * h];
            for (int y = 0; y < h; y++)
            {
                byte alpha = (y % (int)ScanlineSpacing == 0) ? (byte)255 : (byte)0;
                var col = new Color32(0, 0, 0, alpha);
                for (int x = 0; x < w; x++)
                    pixels[y * w + x] = col;
            }
            tex.SetPixels32(pixels);
            tex.Apply(false);
            return tex;
        }

        private static Texture2D GenerateNoise(int w, int h)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            // Repeat so the drawn UV rect can be offset past 1.0 and still tile.
            tex.wrapMode = TextureWrapMode.Repeat;
            RegenerateNoise(tex, w, h);
            return tex;
        }

        // Pre-allocated noise buffer — reused so the one-time generation does not
        // allocate a second large array.
        private static Color32[] _noisePixels;

        private static void RegenerateNoise(Texture2D tex, int w, int h)
        {
            int count = w * h;
            if (_noisePixels == null || _noisePixels.Length != count)
                _noisePixels = new Color32[count];

            for (int i = 0; i < count; i++)
            {
                byte v = (byte)(Random.value * 255f);
                _noisePixels[i] = new Color32(v, v, v, 255);
            }
            // SetPixels32 is significantly faster than SetPixels — it avoids
            // float-to-byte conversion inside Unity and skips a second allocation.
            // Apply(false) skips mipmap regeneration, which noise doesn't need.
            tex.SetPixels32(_noisePixels);
            tex.Apply(false);
        }
    }
}
