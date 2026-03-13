using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using IssaPlugin.Items;
using IssaPlugin.Overlays;
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
            AssetLoader.Load();

            _harmony = new Harmony(PluginInfo.PLUGIN_GUID);
            _harmony.PatchAll(typeof(IssaPluginPlugin).Assembly);
            Log.LogInfo("Harmony patches applied.");

            CourseManager.MatchStateChanged += OnMatchStateChanged;

            gameObject.AddComponent<PlayerBoxOverlay>();
            gameObject.AddComponent<BomberOverlay>();
            gameObject.AddComponent<AC130Overlay>();
            gameObject.AddComponent<FreezeOverlay>();
            gameObject.AddComponent<FreezePhysicsHandler>();
            gameObject.AddComponent<LowGravityOverlay>();
            gameObject.AddComponent<LowGravityHandler>();

            Log.LogInfo("IssaPlugin by Scusemua has loaded.");
        }

        private void OnDestroy()
        {
            CourseManager.MatchStateChanged -= OnMatchStateChanged;
            _harmony?.UnpatchSelf();
            AssetLoader.Unload();
            Log.LogInfo("IssaPlugin unloaded.");
        }

        private void Update()
        {
            if (!_itemNamesRegistered)
            {
                ItemRegistry.RegisterCustomItemNames();
                _itemNamesRegistered = true;
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

            if (keyboard[Configuration.AC130GiveKey.Value].wasPressedThisFrame)
                AC130Item.GiveAC130ToLocalPlayer();

            if (keyboard[Configuration.FreezeGiveKey.Value].wasPressedThisFrame)
                FreezeItem.GiveFreezeToLocalPlayer();

            if (keyboard[Configuration.LowGravityGiveKey.Value].wasPressedThisFrame)
                LowGravityItem.GiveLowGravityToLocalPlayer();

            if (keyboard[Key.F10].wasPressedThisFrame)
                DebugDummies.ToggleDebugDummies();
        }

        private void OnMatchStateChanged(MatchState previousState, MatchState currentState)
        {
            Log.LogDebug($"[MatchState] {previousState} → {currentState}");
        }
    }
}
