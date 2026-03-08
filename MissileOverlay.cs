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

        private static string[] _playerNames = new string[]
        {
            "Lucy",
            "Charlie",
            "Daisy",
            "Ethan",
            "Fiona",
            "George",
            "Harry",
            "Isabella",
            "Jack",
            "Kate",
            "Liam",
            "Mia",
            "Noah",
            "Olivia",
            "Patrick",
            "Quinn",
            "Ryan",
            "Bob",
            "Tony",
            "Ashley",
            "George",
        };

        private void OnGUI()
        {
            if (!PredatorMissileItem.IsSteering && !StealthBomberItem.IsTargeting)
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
                    DrawTargetName(
                        cam,
                        player.transform.position + Vector3.up * 1f,
                        screenH,
                        player.PlayerId.PlayerName
                    );
                }
            }

            for (int i = 0; i < PredatorMissileItem.DebugDummies.Count; i++)
            {
                var dummy = PredatorMissileItem.DebugDummies[i];
                if (dummy == null)
                    continue;
                DrawTargetBox(cam, dummy.transform.position + Vector3.up * 1f, screenH);
                DrawTargetName(
                    cam,
                    dummy.transform.position + Vector3.up * 1f,
                    screenH,
                    _playerNames[i % _playerNames.Length]
                );
            }
        }

        private static void DrawTargetName(Camera cam, Vector3 worldPos, float screenH, string name)
        {
            Vector3 screenPos = cam.WorldToScreenPoint(worldPos);
            if (screenPos.z <= 0f)
                return;

            float x = screenPos.x - BoxWidth * 0.5f;
            float y = screenH - screenPos.y - BoxHeight * 0.5f;
            float t = BorderThickness;

            GUI.Label(new Rect(x, y, BoxWidth, t), name);
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
