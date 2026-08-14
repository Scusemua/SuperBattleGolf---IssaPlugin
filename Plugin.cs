using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using IssaPlugin.Items;
using IssaPlugin.Network;
using IssaPlugin.Overlays;
using IssaPlugin.Patches;
using Mirror;
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
            Log.LogInfo($"IssaPlugin {PluginInfo.PLUGIN_VERSION} is now loading...");

            ModConfig.Initialize(Config);
            AssetLoader.Load();

            _harmony = new Harmony(PluginInfo.PLUGIN_GUID);
            _harmony.PatchAll(typeof(IssaPluginPlugin).Assembly);
            Log.LogInfo("Harmony patches applied.");

            CourseManager.MatchStateChanged += OnMatchStateChanged;

            // Any config change on the host must invalidate ItemConfigSyncer's
            // change-guard so the next sync tick pushes it to clients. This covers
            // changes the mod does not route through the in-game UI — most notably
            // BepInEx re-reading the config file after it is edited on disk.
            Config.SettingChanged += OnConfigSettingChanged;
            Config.ConfigReloaded += OnConfigReloaded;

            gameObject.AddComponent<SpawnWeightsSyncer>();
            gameObject.AddComponent<ItemConfigSyncer>();
            gameObject.AddComponent<VoteManager>();
            gameObject.AddComponent<PlayerBoxOverlay>();
            gameObject.AddComponent<VoteOverlay>();
            gameObject.AddComponent<BomberOverlay>();
            gameObject.AddComponent<AC130Overlay>();
            gameObject.AddComponent<FreezeOverlay>();
            gameObject.AddComponent<FreezePhysicsHandler>();
            gameObject.AddComponent<LowGravityOverlay>();
            gameObject.AddComponent<LowGravityHandler>();
            gameObject.AddComponent<WindStormOverlay>();
            gameObject.AddComponent<SniperScopeOverlay>();
            gameObject.AddComponent<BearOverlay>();
            gameObject.AddComponent<BearHealthBarOverlay>();
            gameObject.AddComponent<GunCrosshairOverlay>();
            gameObject.AddComponent<HitNotificationOverlay>();
            gameObject.AddComponent<ItemWarningOverlay>();
            gameObject.AddComponent<PositionSwapOverlay>();
            gameObject.AddComponent<PoisonOverlay>();
            gameObject.AddComponent<DroneSwarmOverlay>();
            gameObject.AddComponent<GravityGunOverlay>();
            gameObject.AddComponent<RocketTetherOverlay>();
            gameObject.AddComponent<UfoAbductionOverlay>();
            gameObject.AddComponent<JetpackOverlay>();
            gameObject.AddComponent<TeleporterOverlay>();
            gameObject.AddComponent<SpinachOverlay>();
            gameObject.AddComponent<FirstPlaceStarOverlay>();
            gameObject.AddComponent<SpawnConfigUI>();
            gameObject.AddComponent<ShapeShifterManager>();
            gameObject.AddComponent<ShapeShifterOverlay>();

            Log.LogInfo("IssaPlugin by Scusemua has loaded.");
        }

        private void OnDestroy()
        {
            CourseManager.MatchStateChanged -= OnMatchStateChanged;
            Config.SettingChanged -= OnConfigSettingChanged;
            Config.ConfigReloaded -= OnConfigReloaded;
            _harmony?.UnpatchSelf();
            AssetLoader.Unload();
            Log.LogInfo("IssaPlugin unloaded.");
        }

        /// A single config entry changed (in-game UI, another plugin, or BepInEx
        /// applying a value). Invalidate the sync guard so the host re-broadcasts.
        ///
        /// On a client this also fires while applying the host's own sync message,
        /// but ItemConfigSyncer.Broadcast() is a no-op off the server, so clearing
        /// the sentinel there is harmless and cannot feed back to the host.
        private void OnConfigSettingChanged(object sender, SettingChangedEventArgs e) =>
            ItemConfigSyncer.ResetSyncState();

        /// The config file was re-read from disk (BepInEx file watcher). Every entry
        /// may have changed at once, so invalidate the sync guard.
        private void OnConfigReloaded(object sender, EventArgs e) =>
            ItemConfigSyncer.ResetSyncState();

        private void Update()
        {
            if (!_itemNamesRegistered)
                _itemNamesRegistered = ItemRegistry.RegisterCustomItemNames();

            var keyboard = Keyboard.current;
            if (keyboard == null)
                return;

            // Check if player used hotkey to give themself a custom item.
            foreach (var def in ItemRegistry.AllItems)
                if (def.GiveKey != Key.None && keyboard[def.GiveKey].wasPressedThisFrame)
                    ItemHelper.GiveItemToLocalPlayer(def.ItemType, def.MaxUses, def.DisplayName);

            // Keep the Javelin lock-on target fresh every frame while equipped.
            var localInventory = GameManager.LocalPlayerInventory;
            if (localInventory != null)
            {
                var equippedItem = localInventory.GetEffectivelyEquippedItem(true);
                if (equippedItem == ItemRegistry.JavelinItemType)
                {
                    var javelinBridge =
                        NetworkClient.localPlayer?.GetComponent<JavelinNetworkBridge>();
                    javelinBridge?.ClientUpdateLockOn();
                }
            }

            if (keyboard[Key.F10].wasPressedThisFrame)
                DebugDummies.ToggleDebugDummies();
        }

        private void OnMatchStateChanged(MatchState previousState, MatchState currentState)
        {
            Log.LogDebug($"[MatchState] {previousState} → {currentState}");

            // When a new hole begins, force-end any item sessions that survived the
            // scene transition (player objects are DontDestroyOnLoad; OnStopServer
            // only fires on disconnect, not on hole-to-hole scene changes).
            if (currentState != MatchState.HoleOverview)
                return;

            // ── Server-side cleanup ───────────────────────────────────────────
            if (NetworkServer.active)
            {
                foreach (var b in FindObjectsByType<NetworkBridgeBase>(FindObjectsSortMode.None))
                    b.ServerHoleCleanup();
            }

            // ── Client-side cleanup (local player only) ───────────────────────
            var local = NetworkClient.localPlayer;
            if (local != null)
            {
                foreach (var clientNetworkBridge in local.GetComponents<NetworkBridgeBase>())
                    clientNetworkBridge.ClientHoleCleanup();
            }

            // Cancel any in-progress Stealth Bomber targeting UI.
            StealthBomberItem.CancelTargeting();

            // Cancel any in-progress Teleporter targeting UI.
            TeleporterItem.CancelTargeting();

            // ── Shared lock-on detection state ───────────────────────────────
            GunshipLockOnDetectionPatch.ResetTargetingState();

            Log.LogInfo("[MatchState] Hole transition cleanup complete.");
        }
    }
}
