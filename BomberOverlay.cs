using IssaPlugin.Items;
using UnityEngine;

namespace IssaPlugin
{
    public class BomberOverlay : MonoBehaviour
    {
        private static Texture2D _bgTex;
        private GUIStyle _titleStyle;
        private GUIStyle _instructionStyle;

        private void OnGUI()
        {
            if (!StealthBomberItem.IsTargeting)
                return;

            if (_bgTex == null)
            {
                _bgTex = new Texture2D(1, 1);
                _bgTex.SetPixel(0, 0, new Color(0f, 0f, 0f, 0.6f));
                _bgTex.Apply();
            }

            if (_titleStyle == null)
            {
                _titleStyle = new GUIStyle(GUI.skin.label)
                {
                    alignment = TextAnchor.MiddleCenter,
                    fontSize = 22,
                    fontStyle = FontStyle.Bold
                };
                _titleStyle.normal.textColor = new Color(1f, 0.5f, 0f);
            }

            if (_instructionStyle == null)
            {
                _instructionStyle = new GUIStyle(GUI.skin.label)
                {
                    alignment = TextAnchor.MiddleCenter,
                    fontSize = 15,
                    fontStyle = FontStyle.Bold
                };
                _instructionStyle.normal.textColor = Color.white;
            }

            float w = Screen.width;
            float h = Screen.height;

            GUI.DrawTexture(new Rect(0, h - 80, w, 80), _bgTex);

            GUI.Label(
                new Rect(0, h - 75, w, 35),
                "STEALTH BOMBER TARGETING",
                _titleStyle
            );

            GUI.Label(
                new Rect(0, h - 42, w, 30),
                "WASD: Move   |   Q/E: Rotate   |   Click / Enter: Confirm   |   ESC: Cancel",
                _instructionStyle
            );
        }
    }
}
