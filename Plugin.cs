using System.Collections.Generic;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using IssaPlugin.Items;
using IssaPlugin.Patches;
using UnityEngine;
using UnityEngine.InputSystem;

namespace IssaPlugin
{
    [BepInPlugin(PluginInfo.PLUGIN_GUID, PluginInfo.PLUGIN_NAME, PluginInfo.PLUGIN_VERSION)]
    public class IssaPluginPlugin : BaseUnityPlugin
    {
        public static IssaPluginPlugin Instance { get; private set; }
        internal static ManualLogSource Log { get; private set; }

        private Harmony _harmony;
        private bool _itemNamesRegistered;
        private bool _audioLoadStarted;

        private void Awake()
        {
            if (Instance != null)
            {
                Destroy(this);
                return;
            }
            Instance = this;

            Log = base.Logger;
            Log.LogInfo($"IssaPlugin {PluginInfo.PLUGIN_VERSION} loading...");

            Configuration.Initialize(Config);

            _harmony = new Harmony(PluginInfo.PLUGIN_GUID);
            _harmony.PatchAll(typeof(IssaPluginPlugin).Assembly);
            Log.LogInfo("Harmony patches applied.");

            CourseManager.MatchStateChanged += OnMatchStateChanged;

            Log.LogInfo("IssaPlugin by Scusemua has loaded.");
        }

        private void OnDestroy()
        {
            CourseManager.MatchStateChanged -= OnMatchStateChanged;
            _harmony?.UnpatchSelf();
            Log.LogInfo("IssaPlugin unloaded.");
        }

        private void Update()
        {
            if (!_itemNamesRegistered)
            {
                InventoryPatches.RegisterCustomItemNames();
                _itemNamesRegistered = true;
            }

            if (!_audioLoadStarted)
            {
                _audioLoadStarted = true;
                BatItem.LoadHomerunSound();
            }

            var keyboard = Keyboard.current;
            if (keyboard == null)
                return;

            if (keyboard[Configuration.BaseballBatGiveKey.Value].wasPressedThisFrame)
                BatItem.GiveBatToLocalPlayer();

            if (keyboard[Configuration.BomberGiveKey.Value].wasPressedThisFrame)
                StealthBomberItem.GiveBomberToLocalPlayer();

            if (keyboard[Configuration.MissileGiveKey.Value].wasPressedThisFrame)
                PredatorMissileItem.GiveMissileToLocalPlayer();
        }

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

            List<PlayerInfo> remotePlayers = GameManager.RemotePlayers;
            if (remotePlayers == null)
                return;

            float screenH = Screen.height;

            foreach (var player in remotePlayers)
            {
                if (player == null)
                    continue;

                Vector3 worldPos = player.transform.position + Vector3.up * 1f;
                Vector3 screenPos = cam.WorldToScreenPoint(worldPos);

                if (screenPos.z <= 0f)
                    continue;

                float x = screenPos.x - BoxWidth * 0.5f;
                float y = screenH - screenPos.y - BoxHeight * 0.5f;
                float t = BorderThickness;

                // Top
                GUI.DrawTexture(new Rect(x, y, BoxWidth, t), _redTex);
                // Bottom
                GUI.DrawTexture(new Rect(x, y + BoxHeight - t, BoxWidth, t), _redTex);
                // Left
                GUI.DrawTexture(new Rect(x, y, t, BoxHeight), _redTex);
                // Right
                GUI.DrawTexture(new Rect(x + BoxWidth - t, y, t, BoxHeight), _redTex);
            }
        }

        private void OnMatchStateChanged(MatchState previousState, MatchState currentState)
        {
            Log.LogDebug($"[MatchState] {previousState} → {currentState}");
        }
    }
}
