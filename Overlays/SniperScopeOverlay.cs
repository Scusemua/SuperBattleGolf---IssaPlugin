using System.Reflection;
using IssaPlugin.Items;
using UnityEngine;
using UnityEngine.InputSystem;

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

        // PlayerInventory.IsAimingItem { get; private set; } — has private setter.
        private static readonly PropertyInfo IsAimingItemProp = typeof(PlayerInventory).GetProperty(
            "IsAimingItem",
            BindingFlags.Public | BindingFlags.Instance
        );

        private OrbitCameraModule _orbitModule;

        // Orbit module FOV at the moment we entered scope (before any zoom offset).
        // Used to compute the negative offset that hits the absolute target FOV.
        private float _savedBaseFov;
        private bool _fovSaved;
        private float _currentFovOffset;

        // Previous scope state — used to fire enter/exit edge events.
        private bool _prevScoped;

        // Current scroll-adjusted target FOV. Reset to ZoomFov when entering scope.
        private float _targetZoomFov;

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

            // ── Aim animation — driven every frame ──────────────────────────
            // UpdateIsAimingItem is edge-triggered (fires only when right-click
            // changes state). If anything resets IsAimingItem after that single
            // event, the character drops back to idle and never re-raises the
            // gun while the button stays held. Writing the property every frame
            // is the reliable fix. SniperPatches.cs blocks the game's own
            // UpdateIsAimingItem from overriding us while the sniper is equipped.
            if (sniperEquipped && localInfo?.Inventory != null)
            {
                IsAimingItemProp?.SetValue(localInfo.Inventory, wantsScope);
                localInfo.AnimatorIo.SetIsAimingItem(wantsScope);

                if (wantsScope && !_prevScoped)
                {
                    GameplayCameraManager.EnterSwingAimCamera();
                    localInfo.PlayerAudio.PlayGunAimForAllClients(ItemType.ElephantGun);
                    localInfo.SetIsAimingItem(true);
                    localInfo.Movement.InformIsAimingItemChanged();
                }
                else if (!wantsScope && _prevScoped)
                {
                    GameplayCameraManager.ExitSwingAimCamera();
                    localInfo.SetIsAimingItem(false);
                    localInfo.Movement.InformIsAimingItemChanged();
                }
            }
            else if (_prevScoped)
            {
                // Sniper was un-equipped while scope was held — clean up.
                GameplayCameraManager.ExitSwingAimCamera();
                localInfo?.SetIsAimingItem(false);
                localInfo?.Movement.InformIsAimingItemChanged();
            }

            _prevScoped = wantsScope;

            if (_orbitModule == null)
                CameraModuleController.TryGetOrbitModule(out _orbitModule);
            if (_orbitModule == null)
                return;

            // Capture the base FOV the first time we scope in, and reset zoom to default.
            if (wantsScope && !_fovSaved)
            {
                _savedBaseFov  = _orbitModule.FieldOfView;
                _targetZoomFov = Configuration.SniperRifleZoomFov.Value;
                _fovSaved      = true;
            }

            // While scoped, let the scroll wheel nudge _targetZoomFov within [min, max].
            if (wantsScope)
            {
                float scroll = Mouse.current?.scroll.ReadValue().y ?? 0f;
                if (scroll != 0f)
                {
                    // One Windows scroll notch = 120 units; divide to get notch count.
                    _targetZoomFov -= (scroll / 120f) * Configuration.SniperRifleScrollSensitivity.Value;
                    _targetZoomFov  = Mathf.Clamp(
                        _targetZoomFov,
                        Configuration.SniperRifleMinZoomFov.Value,
                        Configuration.SniperRifleMaxZoomFov.Value
                    );
                }
            }

            // targetOffset is negative to zoom in; 0 restores normal FOV.
            float targetOffset = wantsScope
                ? _targetZoomFov - _savedBaseFov
                : 0f;

            _currentFovOffset = Mathf.Lerp(
                _currentFovOffset,
                targetOffset,
                Time.deltaTime * Configuration.SniperRifleZoomSpeed.Value
            );
            _orbitModule.SetFovOffset(_currentFovOffset);

            // Once we've fully unzoomed, stop overriding.
            if (!wantsScope && _fovSaved && Mathf.Abs(_currentFovOffset) < 0.05f)
            {
                _orbitModule.SetFovOffset(0f);
                _fovSaved = false;
            }
        }

        // ── Scope overlay rendering ──────────────────────────────────────────

        private void OnGUI()
        {
            if (!SniperRifleItem.IsScoped || _solidTex == null)
                return;

            DrawScope();
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
            _orbitModule?.SetFovOffset(0f);
            _fovSaved = false;
            SniperRifleItem.IsScoped = false;
        }

        void DrawCenterDot()
        {
            float size = 6f;

            Rect rect = new Rect(
                Screen.width / 2f - size / 2f,
                Screen.height / 2f - size / 2f,
                size,
                size
            );

            GUI.color = Color.red;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
        }

        void DrawScope()
        {
            // If a scope texture was loaded from the bundle, draw it centred.
            // Otherwise fall back to the procedural curtains + crosshairs.
            if (AssetLoader.SniperScopeTexture != null)
            {
                float scopeSize = Mathf.Min(Screen.width, Screen.height) * 0.8f;
                GUI.DrawTexture(
                    new Rect(
                        Screen.width / 2f - scopeSize / 2f,
                        Screen.height / 2f - scopeSize / 2f,
                        scopeSize,
                        scopeSize
                    ),
                    AssetLoader.SniperScopeTexture
                );
                return;
            }

            // ── Procedural fallback ─────────────────────────────────────────
            float sw = Screen.width;
            float sh = Screen.height;
            float cx = sw * 0.5f;
            float cy = sh * 0.5f;
            float radius = sh * 0.45f;

            // Four black curtain panels leave a circular opening.
            DrawRect(0f, 0f, cx - radius, sh, Color.black);
            DrawRect(cx + radius, 0f, sw - (cx + radius), sh, Color.black);
            DrawRect(cx - radius, 0f, radius * 2f, cy - radius, Color.black);
            DrawRect(cx - radius, cy + radius, radius * 2f, sh - (cy + radius), Color.black);

            // Crosshairs
            float gap = radius * 0.06f;
            float arm = radius * 0.30f;
            float thick = Mathf.Max(2f, sw / 600f);

            DrawHLine(cx - gap - arm, cy, arm, thick + 2f, Color.black);
            DrawHLine(cx + gap, cy, arm, thick + 2f, Color.black);
            DrawVLine(cx, cy - gap - arm, arm, thick + 2f, Color.black);
            DrawVLine(cx, cy + gap, arm, thick + 2f, Color.black);

            DrawHLine(cx - gap - arm, cy, arm, thick, Color.white);
            DrawHLine(cx + gap, cy, arm, thick, Color.white);
            DrawVLine(cx, cy - gap - arm, arm, thick, Color.white);
            DrawVLine(cx, cy + gap, arm, thick, Color.white);

            // Mil-dots
            float dotDist = gap + arm * 0.5f;
            float dotR = thick * 1.5f;
            DrawDot(cx - dotDist, cy, dotR);
            DrawDot(cx + dotDist, cy, dotR);
            DrawDot(cx, cy - dotDist, dotR);
            DrawDot(cx, cy + dotDist, dotR);
        }
    }
}
