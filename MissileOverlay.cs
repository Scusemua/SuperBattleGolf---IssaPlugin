using System.Collections.Generic;
using IssaPlugin.Items;
using UnityEngine;

namespace IssaPlugin
{
    public class MissileOverlay : MonoBehaviour
    {
        private static Texture2D _redTex;
        private const float BoxWidth = 40f;
        private const float BoxHeight = 56f;
        private const float BorderThickness = 2f;

        private void OnGUI()
        {
            if (!PredatorMissileItem.IsSteering)
                return;

            var cam = GameManager.Camera;
            if (cam == null)
                return;

            if (_redTex == null)
            {
                _redTex = new Texture2D(1, 1);
                _redTex.SetPixel(0, 0, Color.red);
                _redTex.Apply();
            }

            float screenH = Screen.height;

            List<PlayerInfo> remotePlayers = GameManager.RemotePlayers;
            if (remotePlayers != null)
            {
                foreach (var player in remotePlayers)
                {
                    if (player == null)
                        continue;
                    DrawTargetBox(cam, player.transform.position + Vector3.up * 1f, screenH);
                }
            }

            foreach (var dummy in PredatorMissileItem.DebugDummies)
            {
                if (dummy == null)
                    continue;
                DrawTargetBox(cam, dummy.transform.position + Vector3.up * 1f, screenH);
            }
        }

        private static void DrawTargetBox(Camera cam, Vector3 worldPos, float screenH)
        {
            Vector3 screenPos = cam.WorldToScreenPoint(worldPos);
            if (screenPos.z <= 0f)
                return;

            float x = screenPos.x - BoxWidth * 0.5f;
            float y = screenH - screenPos.y - BoxHeight * 0.5f;
            float t = BorderThickness;

            GUI.DrawTexture(new Rect(x, y, BoxWidth, t), _redTex);
            GUI.DrawTexture(new Rect(x, y + BoxHeight - t, BoxWidth, t), _redTex);
            GUI.DrawTexture(new Rect(x, y, t, BoxHeight), _redTex);
            GUI.DrawTexture(new Rect(x + BoxWidth - t, y, t, BoxHeight), _redTex);
        }
    }
}
