using UnityEngine;

namespace IssaPlugin.Overlays
{
    /// Static HUD overlay displayed during an active UFO session.
    /// Shows remaining laser uses and session time remaining.
    /// Activated/deactivated by UFONetworkBridge.RunLocalSession.
    public static class UFOOverlay
    {
        private static GameObject _go;
        private static UFOOverlayRenderer _renderer;

        public static void SetActive(bool active, int totalLaserUses)
        {
            if (active)
            {
                if (_go == null)
                {
                    _go = new GameObject("UFOOverlay");
                    Object.DontDestroyOnLoad(_go);
                    _renderer = _go.AddComponent<UFOOverlayRenderer>();
                }

                _renderer.TotalLaserUses = totalLaserUses;
                _renderer.RemainingLaserUses = totalLaserUses;
                _renderer.TimeRemaining = Configuration.UFODuration.Value;
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
    }

    internal class UFOOverlayRenderer : MonoBehaviour
    {
        public int TotalLaserUses;
        public int RemainingLaserUses;
        public float TimeRemaining;

        private GUIStyle _labelStyle;
        private GUIStyle _instructionStyle;
        private GUIStyle _dotStyle;
        private Texture2D _bgTex;

        private void Awake() { }

        private void EnsureStyles()
        {
            if (_labelStyle == null)
            {
                _labelStyle = new GUIStyle
                {
                    fontSize = 32,
                    fontStyle = FontStyle.Bold,
                    normal = { textColor = Color.white },
                    alignment = TextAnchor.MiddleCenter,
                };
            }

            if (_dotStyle == null)
            {
                _dotStyle = new GUIStyle
                {
                    fontSize = 28,
                    fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.MiddleCenter,
                };
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

            if (_bgTex == null)
            {
                _bgTex = new Texture2D(1, 1);
                _bgTex.SetPixel(0, 0, new Color(0f, 0f, 0f, 0.6f));
                _bgTex.Apply();
            }
        }

        private void OnGUI()
        {
            EnsureStyles();

            float sw = Screen.width;
            float sh = Screen.height;

            // ── Session timer — top-centre ────────────────────────────────────
            int secs = Mathf.CeilToInt(Mathf.Max(0f, TimeRemaining));
            string timerText = $"UFO  {secs / 60:D2}:{secs % 60:D2}";
            GUI.Label(new Rect(sw * 0.5f - 80f, 16f, 160f, 36f), timerText, _labelStyle);

            // ── Laser uses — bottom-centre ────────────────────────────────────
            float dotSize = 32f;
            float gap = 8f;
            float totalWidth = TotalLaserUses * dotSize + (TotalLaserUses - 1) * gap;
            float startX = sw * 0.5f - totalWidth * 0.5f;
            float dotY = sh - 160f;

            for (int i = 0; i < TotalLaserUses; i++)
            {
                _dotStyle.normal.textColor =
                    i < RemainingLaserUses
                        ? new Color(0.4f, 0.8f, 1f) // available — blue-white
                        : new Color(0.3f, 0.3f, 0.3f); // spent — grey
                GUI.Label(
                    new Rect(startX + i * (dotSize + gap), dotY, dotSize, dotSize),
                    "●",
                    _dotStyle
                );
            }

            // ── "LASER" label below dots ──────────────────────────────────────
            _labelStyle.fontSize = 14;
            GUI.Label(new Rect(sw * 0.5f - 40f, dotY + dotSize, 80f, 24f), "LASER", _labelStyle);
            _labelStyle.fontSize = 22;

            // Bottom bar.
            if (_bgTex != null)
                GUI.DrawTexture(new Rect(0, sh - 160, sw, 160), _bgTex);
            GUI.Label(
                new Rect(0, sh - 42, sw, 30),
                "Click: Fire Laser   |   Space: Exit",
                _instructionStyle
            );
        }
    }
}
