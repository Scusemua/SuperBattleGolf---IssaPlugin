using System.Collections.Generic;
using IssaPlugin.Items;
using UnityEngine;

namespace IssaPlugin.Overlays
{
    public class PlayerBoxOverlay : MonoBehaviour
    {
        private static Texture2D _redTex;
        private static Texture2D _greenTex;
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

        // Lazily cached references to the local player's bridges.
        private AC130NetworkBridge _localAC130Bridge;
        private MissileNetworkBridge _localMissileBridge;

        private AC130NetworkBridge LocalAC130Bridge
        {
            get
            {
                if (_localAC130Bridge != null)
                    return _localAC130Bridge;
                var movement = GameManager.LocalPlayerMovement;
                if (movement != null)
                    _localAC130Bridge = movement.GetComponent<AC130NetworkBridge>();
                return _localAC130Bridge;
            }
        }

        private MissileNetworkBridge LocalMissileBridge
        {
            get
            {
                if (_localMissileBridge != null)
                    return _localMissileBridge;
                var movement = GameManager.LocalPlayerMovement;
                if (movement != null)
                    _localMissileBridge = movement.GetComponent<MissileNetworkBridge>();
                return _localMissileBridge;
            }
        }

        private bool ShouldShowGUI() =>
            StealthBomberItem.IsTargeting
            || (LocalMissileBridge != null && LocalMissileBridge.IsSteering)
            || (LocalAC130Bridge != null && LocalAC130Bridge.LocalSessionActive);

        private void OnGUI()
        {
            if (!ShouldShowGUI())
                return;

            // Use the gunship camera for WorldToScreenPoint when the local
            // player is in an AC130 session, so overlay boxes project correctly.
            // Fall back to the game camera at all other times.
            // Both checks are against the local player's bridge instance so
            // other players' concurrent sessions don't affect this client.
            var localAC130 = LocalAC130Bridge;
            var cam =
                (localAC130 != null && localAC130.LocalSessionActive)
                    ? localAC130.LocalGunshipCamera ?? GameManager.Camera
                    : GameManager.Camera;

            if (cam == null)
                return;

            EnsureTextures();

            float screenH = Screen.height;

            // Local player — green box.
            var localPlayerInfo = GameManager.LocalPlayerInfo;
            if (localPlayerInfo != null)
            {
                DrawTargetBox(
                    cam,
                    localPlayerInfo.transform.position + Vector3.up * 1f,
                    screenH,
                    _greenTex
                );
                DrawTargetName(
                    cam,
                    localPlayerInfo.transform.position + Vector3.up * 1f,
                    screenH,
                    localPlayerInfo.PlayerId.PlayerName + " (YOU)"
                );
            }

            // Remote players — red boxes.
            List<PlayerInfo> remotePlayers = GameManager.RemotePlayers;
            if (remotePlayers != null)
            {
                foreach (var player in remotePlayers)
                {
                    if (player == null)
                        continue;
                    DrawTargetBox(
                        cam,
                        player.transform.position + Vector3.up * 1f,
                        screenH,
                        _redTex
                    );
                    DrawTargetName(
                        cam,
                        player.transform.position + Vector3.up * 1f,
                        screenH,
                        player.PlayerId.PlayerName
                    );
                }
            }

            // Debug dummies — red boxes.
            for (int i = 0; i < DebugDummies.DebugDummiesList.Count; i++)
            {
                var dummy = DebugDummies.DebugDummiesList[i];
                if (dummy == null)
                    continue;
                DrawTargetBox(cam, dummy.transform.position + Vector3.up * 1f, screenH, _redTex);
                DrawTargetName(
                    cam,
                    dummy.transform.position + Vector3.up * 1f,
                    screenH,
                    _playerNames[i % _playerNames.Length]
                );
            }
        }

        private static void EnsureTextures()
        {
            if (_redTex == null)
            {
                _redTex = new Texture2D(1, 1);
                _redTex.SetPixel(0, 0, Color.red);
                _redTex.Apply();
            }

            if (_greenTex == null)
            {
                _greenTex = new Texture2D(1, 1);
                _greenTex.SetPixel(0, 0, Color.green);
                _greenTex.Apply();
            }
        }

        private static void DrawTargetName(Camera cam, Vector3 worldPos, float screenH, string name)
        {
            Vector3 screenPos = cam.WorldToScreenPoint(worldPos);
            if (screenPos.z <= 0f)
                return;

            float x = screenPos.x - BoxWidth * 0.5f;
            float y = screenH - screenPos.y - BoxHeight * 0.5f;

            GUI.Label(new Rect(x, y - 20f, BoxWidth * 4f, 24f), name);
        }

        private static void DrawTargetBox(
            Camera cam,
            Vector3 worldPos,
            float screenH,
            Texture2D tex
        )
        {
            Vector3 screenPos = cam.WorldToScreenPoint(worldPos);
            if (screenPos.z <= 0f)
                return;

            float x = screenPos.x - BoxWidth * 0.5f;
            float y = screenH - screenPos.y - BoxHeight * 0.5f;
            float t = BorderThickness;

            GUI.DrawTexture(new Rect(x, y, BoxWidth, t), tex);
            GUI.DrawTexture(new Rect(x, y + BoxHeight - t, BoxWidth, t), tex);
            GUI.DrawTexture(new Rect(x, y, t, BoxHeight), tex);
            GUI.DrawTexture(new Rect(x + BoxWidth - t, y, t, BoxHeight), tex);
        }
    }
}
