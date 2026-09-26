using IssaPlugin.Items;
using UnityEngine;

namespace IssaPlugin.Overlays
{
    /// How to use the grappling hook. Shown while it is equipped, and while a
    /// rope is still attached after the last use.
    public class GrapplingHookOverlay : MonoBehaviour
    {
        private GUIStyle _titleStyle;
        private GUIStyle _lineStyle;
        private Texture2D _panelTex;

        private const float ReferenceHeight = 1080f;
        private const float PanelWidth = 860f;
        private const float MarginX = 28f;
        private const float MarginY = 110f;
        private const float Pad = 22f;
        private const float TitleHeight = 52f;
        private const float LineHeight = 48f;
        private const int TitleFontSize = 36;
        private const int LineFontSize = 28;

        private static readonly string[] Lines =
        {
            "Left click - attach to what you aim at",
            "Once hooked, left click - reel in",
            "Right click - reel out",
            "R - dismount",
        };

        private static readonly string[] AttachedLines =
        {
            "Left click - reel in",
            "Right click - reel out",
            "R - dismount",
        };

        private static float PanelHeight(int lineCount) =>
            Pad + TitleHeight + lineCount * LineHeight + Pad;

        private static float UiScale => Mathf.Max(1f, Screen.height / ReferenceHeight);

        private void OnGUI()
        {
            if (Event.current.type != EventType.Repaint)
                return;

            var inventory = GameManager.LocalPlayerInfo?.Inventory;
            bool hasItem =
                inventory != null
                && inventory.GetEffectivelyEquippedItem(true) == ItemRegistry.GrapplingHookItemType;
            if (!hasItem && !GrapplingHookSession.IsAttached)
                return;

            string[] lines = GrapplingHookSession.IsAttached ? AttachedLines : Lines;
            EnsureStyles();

            float scale = UiScale;
            float panelW = PanelWidth * scale;
            float panelH = PanelHeight(lines.Length) * scale;
            var panel = new Rect(
                MarginX * scale,
                Screen.height - (MarginY * scale) - panelH,
                panelW,
                panelH
            );

            GUI.color = new Color(0f, 0f, 0f, 0.62f);
            GUI.DrawTexture(panel, _panelTex, ScaleMode.StretchToFill);
            GUI.color = Color.white;

            float pad = Pad * scale;
            float titleH = TitleHeight * scale;
            GUI.Label(
                new Rect(panel.x + pad, panel.y + pad, panel.width - pad * 2f, titleH),
                "GRAPPLING HOOK",
                _titleStyle
            );

            float lineH = LineHeight * scale;
            float y = panel.y + pad + titleH;
            for (int i = 0; i < lines.Length; i++)
            {
                GUI.Label(
                    new Rect(panel.x + pad, y, panel.width - pad * 2f, lineH),
                    lines[i],
                    _lineStyle
                );
                y += lineH;
            }
        }

        private void EnsureStyles()
        {
            if (_panelTex == null)
            {
                _panelTex = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
                _panelTex.SetPixel(0, 0, Color.white);
                _panelTex.Apply();
            }

            int titleSize = Mathf.RoundToInt(TitleFontSize * UiScale);
            int lineSize = Mathf.RoundToInt(LineFontSize * UiScale);

            if (_titleStyle == null || _titleStyle.fontSize != titleSize)
            {
                _titleStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = titleSize,
                    fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.MiddleLeft,
                };
                _titleStyle.normal.textColor = new Color(0.85f, 0.92f, 1f, 1f);
            }

            if (_lineStyle == null || _lineStyle.fontSize != lineSize)
            {
                _lineStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = lineSize,
                    alignment = TextAnchor.MiddleLeft,
                };
                _lineStyle.normal.textColor = Color.white;
            }
        }

        private void OnDestroy()
        {
            if (_panelTex != null)
                Destroy(_panelTex);
        }
    }
}
