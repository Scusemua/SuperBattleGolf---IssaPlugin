using IssaPlugin.Items;
using UnityEngine;

namespace IssaPlugin.Overlays
{
    /// <summary>
    /// Shows the authoritative remaining Power Jammer duration on every client.
    /// Affected clients also receive a subtle grey vignette; an immune activator
    /// still sees the timer so they know how long opponents remain disrupted.
    /// </summary>
    public class PowerJammerOverlay : MonoBehaviour
    {
        public static PowerJammerOverlay Instance { get; private set; }
        public static bool IsActive => Instance != null && Instance._active;

        private bool _active;
        private bool _locallyImmune;
        private float _startTime;
        private float _duration;
        private GUIStyle _labelStyle;

        private static readonly Color FillColor = new Color(0.55f, 0.58f, 0.62f, 0.95f);
        private static readonly Color BackgroundColor = new Color(0f, 0f, 0f, 0.6f);
        private static readonly Color VignetteColor = new Color(0.18f, 0.2f, 0.22f, 0.12f);

        private void Awake() => Instance = this;

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        public void SetActive(
            bool active,
            float duration = 0f,
            bool locallyImmune = false
        )
        {
            _active = active;
            _locallyImmune = locallyImmune;
            if (active)
            {
                _duration = duration;
                _startTime = Time.time;
            }
        }

        private void OnGUI()
        {
            if (!_active)
                return;

            Color savedColor = GUI.color;

            if (!_locallyImmune)
            {
                GUI.color = VignetteColor;
                GUI.DrawTexture(
                    new Rect(0f, 0f, Screen.width, Screen.height),
                    Texture2D.whiteTexture
                );
            }

            float elapsed = Time.time - _startTime;
            float fraction = _duration > 0f
                ? Mathf.Clamp01(1f - elapsed / _duration)
                : 0f;
            float remaining = Mathf.Max(0f, _duration - elapsed);

            float barW = EffectBarLayout.GetBarWidth();
            float barH = EffectBarLayout.BarHeight;
            float barX = EffectBarLayout.GetBarX();
            int slot =
                (FreezeItem.IsFrozen ? 1 : 0)
                + (LowGravityItem.IsActive ? 1 : 0)
                + (WindStormOverlay.IsActive ? 1 : 0);
            float barY = EffectBarLayout.GetBarY(slot);

            GUI.color = BackgroundColor;
            GUI.DrawTexture(new Rect(barX, barY, barW, barH), Texture2D.whiteTexture);
            GUI.color = FillColor;
            GUI.DrawTexture(
                new Rect(barX, barY, barW * fraction, barH),
                Texture2D.whiteTexture
            );

            _labelStyle ??= new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 13,
                fontStyle = FontStyle.Bold,
            };
            _labelStyle.normal.textColor = Color.white;
            GUI.color = Color.white;
            string immunitySuffix = _locallyImmune ? " (Immune)" : string.Empty;
            GUI.Label(
                new Rect(barX, barY, barW, barH),
                $"Power Jammer{immunitySuffix}:  {remaining:F1}s",
                _labelStyle
            );

            GUI.color = savedColor;
        }
    }
}
