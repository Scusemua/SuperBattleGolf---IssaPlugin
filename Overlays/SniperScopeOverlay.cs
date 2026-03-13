using IssaPlugin.Items;
using UnityEngine;

namespace IssaPlugin.Overlays
{
    /// <summary>
    /// Manages the sniper scope visuals for the local player:
    ///
    ///   • Update() — tracks right-click (IsHoldingAimSwing) when the sniper is
    ///                equipped, sets SniperRifleItem.IsScoped, and lerps the main
    ///                camera FOV in/out.
    ///
    ///   • OnGUI()  — when scoped, fills the screen with four black curtain panels
    ///                leaving a circular opening in the centre, then draws crosshairs
    ///                and simple mil-dot marks.
    ///
    /// Added to the Plugin's persistent gameObject in Plugin.cs.
    /// </summary>
    public class SniperScopeOverlay : MonoBehaviour
    {
        public static SniperScopeOverlay Instance { get; private set; }

        // Saved FOV so we can restore it when un-scoping.
        private float _savedFov;
        private bool _fovSaved;
        private float _currentFov = 60f;

        // Solid white 1×1 texture — tinted via GUI.color for all drawing.
        private Texture2D _solidTex;

        private void Awake() => Instance = this;

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
            RestoreFov();
            if (_solidTex != null)
                Destroy(_solidTex);
        }

        private void Start()
        {
            _solidTex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            _solidTex.SetPixel(0, 0, Color.white);
            _solidTex.Apply();
        }

        // ── Per-frame logic ──────────────────────────────────────────────────

        private void Update()
        {
            var localInfo = GameManager.LocalPlayerInfo;
            bool sniperEquipped =
                localInfo?.Inventory != null
                && localInfo.Inventory.GetEffectivelyEquippedItem(true)
                    == SniperRifleItem.SniperRifleItemType;

            bool wantsScope = sniperEquipped && (localInfo?.Input?.IsHoldingAimSwing ?? false);
            SniperRifleItem.IsScoped = wantsScope;

            var cam = GameManager.Camera;
            if (cam == null)
                return;

            // Capture the current FOV the first time we scope in.
            if (wantsScope && !_fovSaved)
            {
                _savedFov = cam.fieldOfView;
                _currentFov = _savedFov;
                _fovSaved = true;
            }

            float targetFov = wantsScope
                ? Configuration.SniperRifleZoomFov.Value
                : (_fovSaved ? _savedFov : cam.fieldOfView);

            _currentFov = Mathf.Lerp(
                _currentFov,
                targetFov,
                Time.deltaTime * Configuration.SniperRifleZoomSpeed.Value
            );
            cam.fieldOfView = _currentFov;

            // Once we've fully returned to the saved FOV, stop overriding.
            if (!wantsScope && _fovSaved && Mathf.Abs(_currentFov - _savedFov) < 0.05f)
            {
                cam.fieldOfView = _savedFov;
                _fovSaved = false;
            }
        }

        // ── Scope overlay rendering ──────────────────────────────────────────

        private void OnGUI()
        {
            if (!SniperRifleItem.IsScoped || _solidTex == null)
                return;

            float sw = Screen.width;
            float sh = Screen.height;
            float cx = sw * 0.5f;
            float cy = sh * 0.5f;
            float radius = sh * 0.45f; // scope circle fills most of screen height

            // ── Four black curtain panels (leave a square gap around the circle) ──
            DrawRect(0f, 0f, cx - radius, sh, Color.black); // left
            DrawRect(cx + radius, 0f, sw - (cx + radius), sh, Color.black); // right
            DrawRect(cx - radius, 0f, radius * 2f, cy - radius, Color.black); // top
            DrawRect(cx - radius, cy + radius, radius * 2f, sh - (cy + radius), Color.black); // bottom

            // ── Thin dark vignette border at the circle edge ──────────────────
            float border = radius * 0.05f;
            // re-draw a slightly larger version of each curtain to soften the hard edge
            var oldColor = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.5f);
            GUI.DrawTexture(
                new Rect(
                    cx - radius - border,
                    cy - radius - border,
                    (radius + border) * 2f,
                    border * 2f
                ),
                _solidTex
            );
            GUI.DrawTexture(
                new Rect(
                    cx - radius - border,
                    cy + radius - border,
                    (radius + border) * 2f,
                    border * 2f
                ),
                _solidTex
            );
            GUI.color = oldColor;

            // ── Crosshairs ────────────────────────────────────────────────────
            float gap = radius * 0.06f; // gap around centre point
            float arm = radius * 0.30f; // arm length
            float thick = Mathf.Max(2f, sw / 600f);

            // Black outline (thicker)
            DrawHLine(cx - gap - arm, cy, arm, thick + 2f, Color.black);
            DrawHLine(cx + gap, cy, arm, thick + 2f, Color.black);
            DrawVLine(cx, cy - gap - arm, arm, thick + 2f, Color.black);
            DrawVLine(cx, cy + gap, arm, thick + 2f, Color.black);

            // White inner lines
            DrawHLine(cx - gap - arm, cy, arm, thick, Color.white);
            DrawHLine(cx + gap, cy, arm, thick, Color.white);
            DrawVLine(cx, cy - gap - arm, arm, thick, Color.white);
            DrawVLine(cx, cy + gap, arm, thick, Color.white);

            // ── Mil-dots ──────────────────────────────────────────────────────
            float dotDist = gap + arm * 0.5f;
            float dotR = thick * 1.5f;
            DrawDot(cx - dotDist, cy, dotR);
            DrawDot(cx + dotDist, cy, dotR);
            DrawDot(cx, cy - dotDist, dotR);
            DrawDot(cx, cy + dotDist, dotR);
        }

        // ── Drawing primitives ────────────────────────────────────────────────

        private void DrawRect(float x, float y, float w, float h, Color color)
        {
            if (w <= 0f || h <= 0f)
                return;
            var prev = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(new Rect(x, y, w, h), _solidTex);
            GUI.color = prev;
        }

        private void DrawHLine(float x, float cy, float length, float thick, Color color)
        {
            DrawRect(x, cy - thick * 0.5f, length, thick, color);
        }

        private void DrawVLine(float cx, float y, float length, float thick, Color color)
        {
            DrawRect(cx - thick * 0.5f, y, thick, length, color);
        }

        private void DrawDot(float cx, float cy, float r)
        {
            DrawRect(cx - r, cy - r, r * 2f, r * 2f, Color.white);
        }

        private void RestoreFov()
        {
            if (!_fovSaved)
                return;
            var cam = GameManager.Camera;
            if (cam != null)
                cam.fieldOfView = _savedFov;
            _fovSaved = false;
            SniperRifleItem.IsScoped = false;
        }
    }
}
