using IssaPlugin.Items;
using UnityEngine;

namespace IssaPlugin.Overlays
{
    /// <summary>
    /// Minimal HUD shown to the player who summoned the drone swarm.
    /// Displays how many drones are still active as pips that disappear
    /// on destruction, with a military-tech colour scheme.
    ///
    /// No session timer is shown because the swarm ends only when all drones
    /// are destroyed (not on a fixed timeout).
    ///
    /// Activated via DroneSwarmOverlay.SetActive(true, droneCount) and updated
    /// by DroneSwarmNetworkBridge when it receives DroneDiedClientMessage.
    ///
    /// Added to the Plugin's persistent GameObject in Plugin.cs.
    /// </summary>
    public class DroneSwarmOverlay : MonoBehaviour
    {
        public static DroneSwarmOverlay Instance { get; private set; }

        // ── State ──────────────────────────────────────────────────────────────
        private bool _active;
        private int _totalDrones;
        private int _aliveDrones;

        // ── Textures ───────────────────────────────────────────────────────────
        private Texture2D _solidTex;

        // ── Styles ────────────────────────────────────────────────────────────
        private GUIStyle _warningStyle;
        private const int _warningStyleFontSize = 24;
        private GUIStyle _pipStyle;
        private const int _pipStyleFontSize = 36;
        private GUIStyle _labelStyle;
        private const int _labelStyleFontSize = 20;

        // ── Animation state ───────────────────────────────────────────────────
        private float _time;
        private float _deltaTime;
        private float _sw,
            _sh;

        // Drone-destroyed flash
        private float _killFlashAlpha;
        private const float KillFlashDecay = 3.5f;

        // Colours
        private static readonly Color DroneBlue = new Color(0.10f, 0.60f, 0.90f, 1f);
        private static readonly Color DroneBlueDim = new Color(0.10f, 0.60f, 0.90f, 0.45f);
        private static readonly Color DeadGrey = new Color(0.25f, 0.25f, 0.25f, 0.55f);
        private static readonly Color WarningCyan = new Color(0.20f, 0.90f, 0.95f, 1f);

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

        public void SetActive(bool active, int droneCount)
        {
            _active = active;
            _totalDrones = droneCount;
            _aliveDrones = droneCount;
        }

        public void DecrementDroneCount()
        {
            _aliveDrones = Mathf.Max(0, _aliveDrones - 1);
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

            // ── Drone-kill flash (blue tint on destruction) ────────────────────
            if (_killFlashAlpha > 0f)
            {
                GUI.color = new Color(
                    DroneBlue.r,
                    DroneBlue.g,
                    DroneBlue.b,
                    Mathf.Clamp01(_killFlashAlpha) * 0.25f
                );
                GUI.DrawTexture(new Rect(0, 0, sw, sh), _solidTex);
                GUI.color = Color.white;
            }

            // ── Corner brackets ────────────────────────────────────────────────
            float pulse = 0.5f + 0.5f * Mathf.Sin(_time * 4f);
            DrawCornerBrackets(sw, sh, pulse);

            // ── Drone pip indicators (top-centre) ──────────────────────────────
            DrawDronePips(sw, sh);

            // ── Bottom bar ────────────────────────────────────────────────────
            DrawBottomBar(sw, sh);
        }

        // ── Draw helpers ──────────────────────────────────────────────────────

        private void DrawDronePips(float sw, float sh)
        {
            if (_totalDrones <= 0)
                return;

            const float PipSize = 34f;
            const float PipGap = 6f;
            float totalW = _totalDrones * PipSize + (_totalDrones - 1) * PipGap;
            float startX = sw * 0.5f - totalW * 0.5f;
            float pipY = 14f;

            for (int i = 0; i < _totalDrones; i++)
            {
                bool alive = i < _aliveDrones;

                if (alive)
                {
                    float pulse = 0.75f + 0.25f * Mathf.Sin(_time * 3f + i * 0.8f);
                    _pipStyle.normal.textColor = new Color(
                        DroneBlue.r * pulse,
                        DroneBlue.g,
                        DroneBlue.b,
                        1f
                    );
                }
                else
                {
                    _pipStyle.normal.textColor = DeadGrey;
                }

                GUI.Label(
                    new Rect(startX + i * (PipSize + PipGap), pipY, PipSize, PipSize),
                    "▲",
                    _pipStyle
                );
            }

            _labelStyle.normal.textColor = DroneBlueDim;
            string label =
                _aliveDrones == 0
                    ? "ALL DRONES DESTROYED"
                    : $"{_aliveDrones} DRONE{(_aliveDrones == 1 ? "" : "S")} ACTIVE";
            GUI.Label(new Rect(0, pipY + PipSize + 2f, sw, 20f), label, _labelStyle);
        }

        private void DrawCornerBrackets(float sw, float sh, float pulse)
        {
            const float Size = 44f;
            const float Thick = 2f;

            Color c = new Color(DroneBlue.r, DroneBlue.g, DroneBlue.b, pulse * 0.85f);
            DrawCornerBracket(0, 0, Size, Thick, true, true, c);
            DrawCornerBracket(sw - Size, 0, Size, Thick, false, true, c);
            DrawCornerBracket(0, sh - Size, Size, Thick, true, false, c);
            DrawCornerBracket(sw - Size, sh - Size, Size, Thick, false, false, c);
        }

        private void DrawBottomBar(float sw, float sh)
        {
            GUI.color = new Color(DroneBlue.r, DroneBlue.g, DroneBlue.b, 0.50f);
            DrawRect(0, sh - 75f, sw, 75f);
            GUI.color = Color.white;

            _warningStyle.normal.textColor = new Color(0.85f, 0.97f, 1f, 0.95f);
            GUI.Label(
                new Rect(0, sh - 55f, sw, _warningStyleFontSize),
                "DRONE SWARM",
                _warningStyle
            );

            _labelStyle.normal.textColor = DroneBlueDim;
            GUI.Label(
                new Rect(0, sh - 25f, sw, _labelStyleFontSize),
                "Drones are circling overhead. Watch out!",
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
                    fontSize = _warningStyleFontSize,
                    fontStyle = FontStyle.Bold,
                };
                _warningStyle.normal.textColor = Color.white;
            }

            if (_pipStyle == null)
            {
                _pipStyle = new GUIStyle(GUI.skin.label)
                {
                    alignment = TextAnchor.MiddleCenter,
                    fontSize = _pipStyleFontSize,
                    fontStyle = FontStyle.Bold,
                };
                _pipStyle.normal.textColor = DroneBlue;
            }

            if (_labelStyle == null)
            {
                _labelStyle = new GUIStyle(GUI.skin.label)
                {
                    alignment = TextAnchor.MiddleCenter,
                    fontSize = _labelStyleFontSize,
                    fontStyle = FontStyle.Bold,
                };
                _labelStyle.normal.textColor = DroneBlueDim;
            }
        }
    }
}
