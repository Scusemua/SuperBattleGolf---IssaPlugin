using UnityEngine;

namespace IssaPlugin.Overlays
{
    /// <summary>
    /// Shows a single unobtrusive banner in the bottom-left corner when
    /// <see cref="Network.UpdateChecker"/> finds a newer release on GitHub.
    /// It fades out on its own, and carries a close button so it can be dismissed
    /// immediately.
    ///
    /// Bottom-left rather than bottom-right: the right side is occupied by the
    /// player's hotbar. The panel sits above GolfCartLauncherModeOverlay's own
    /// bottom-left panel so the two never overlap when both are on screen.
    ///
    /// Added to the Plugin's persistent GameObject in Plugin.cs.
    /// </summary>
    public class UpdateAvailableOverlay : MonoBehaviour
    {
        public static UpdateAvailableOverlay Instance { get; private set; }

        private const float FadeDuration = 1.5f;

        /// <summary>Design resolution the fixed sizes below are tuned against.</summary>
        private const float ReferenceHeight = 1080f;

        private string _message;
        private float _expireTime;

        private GUIStyle _style;
        private GUIStyle _closeStyle;

        /// <summary>Scale the styles were last built at, so a resolution change rebuilds them.</summary>
        private float _styleScale;

        /// <summary>
        /// Matches SpawnerWindow.CurrentScale: scale by height against a 1080p reference
        /// so the notice keeps its apparent size on a 1440p or 4K display instead of
        /// shrinking into the corner. Height only -- an ultra-wide monitor is wider but
        /// no taller, so scaling by width would oversize it on a 32:9 display.
        /// </summary>
        private static float CurrentScale => Mathf.Max(1f, Screen.height / ReferenceHeight);

        // ── Unity lifecycle ───────────────────────────────────────────────────

        private void Awake() => Instance = this;

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        // ── Public API ────────────────────────────────────────────────────────

        public void Show(string currentVersion, string latestVersion)
        {
            _message =
                $"IssaMod update available: v{latestVersion}  (you have v{currentVersion})\n"
                + "Get it at github.com/Scusemua/SuperBattleGolf---IssaPlugin/releases";

            // Left unset: the countdown starts on the first frame this actually draws,
            // not here. The check finishes a few seconds into startup, while the game is
            // still loading, so a timer started now could burn its whole duration behind
            // a loading screen and the player would never see the notice.
            _expireTime = 0f;
        }

        // ── Rendering ─────────────────────────────────────────────────────────

        private void OnGUI()
        {
            if (_message == null)
                return;

            // Arm the countdown the first time we actually paint a frame, so the
            // duration is time the notice was genuinely on screen rather than time
            // spent behind a loading screen. Repaint only, since a Layout pass does
            // not mean anything reached the display -- but still fall through and
            // draw on every event, because IMGUI needs each control issued on every
            // pass for its clicks to register.
            if (_expireTime == 0f && Event.current.type == EventType.Repaint)
                _expireTime = Time.time + ModConfig.Global.UpdateNoticeDuration.Value;

            // Before the first repaint there is no deadline yet: draw at full opacity.
            float remaining = _expireTime == 0f
                ? ModConfig.Global.UpdateNoticeDuration.Value
                : _expireTime - Time.time;

            if (remaining <= 0f)
            {
                _message = null;
                return;
            }

            float scale = CurrentScale;
            EnsureStyle(scale);

            float alpha = remaining < FadeDuration ? remaining / FadeDuration : 1f;

            float width = 430f * scale;
            float height = 46f * scale;
            float pad = 14f * scale;

            // Clear GolfCartLauncherModeOverlay's bottom-left panel (its own margin plus
            // panel height, both at the same scale) so the two never overlap.
            const float GolfCartPanelTop = 210f;
            float bottomOffset = Mathf.Max(pad, GolfCartPanelTop * scale);

            var rect = new Rect(pad, Screen.height - height - bottomOffset, width, height);

            // The close button sits inside the panel's top-right corner.
            float closeSize = 20f * scale;
            float closeInset = 4f * scale;
            var closeRect = new Rect(
                rect.xMax - closeSize - closeInset,
                rect.y + closeInset,
                closeSize,
                closeSize
            );

            var previousColor = GUI.color;

            GUI.color = new Color(0f, 0f, 0f, 0.45f * alpha);
            GUI.Box(rect, GUIContent.none);

            GUI.color = new Color(1f, 1f, 1f, alpha);
            _style.normal.textColor = new Color(1f, 0.9f, 0.45f, alpha);
            // Keep the label clear of the close button so the text never runs under it.
            var textRect = new Rect(
                rect.x + closeInset,
                rect.y,
                rect.width - closeSize - closeInset * 2f,
                rect.height
            );
            GUI.Label(textRect, _message, _style);

            // Draw the button at full opacity: a faded button still accepts clicks, and
            // a control that is invisible but live is worse than one that stays legible.
            GUI.color = Color.white;
            if (GUI.Button(closeRect, "x", _closeStyle))
            {
                _message = null;
                GUI.color = previousColor;
                return;
            }

            GUI.color = previousColor;
        }

        private void EnsureStyle(float scale)
        {
            // Rebuild when the scale changes so a resolution switch resizes the font
            // rather than leaving it at the size built for the previous display.
            if (_style != null && Mathf.Approximately(_styleScale, scale))
                return;

            _styleScale = scale;
            _style = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = Mathf.RoundToInt(13f * scale),
                fontStyle = FontStyle.Bold,
                wordWrap = true,
            };

            _closeStyle = new GUIStyle(GUI.skin.button)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = Mathf.RoundToInt(12f * scale),
                fontStyle = FontStyle.Bold,
                padding = new RectOffset(0, 0, 0, 0),
            };
        }
    }
}
