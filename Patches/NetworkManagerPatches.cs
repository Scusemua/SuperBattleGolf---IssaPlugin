using HarmonyLib;
using IssaPlugin.Items;
using Mirror;
using UnityEngine;

namespace IssaPlugin.Patches
{
    /// Registers all custom networked prefabs and NetworkMessage handlers with Mirror
    /// immediately after the client transport connects.
    ///
    /// Hook point: BNetworkManager.OnStartClient() — fires on every client
    /// (including the listen-server host) before any server spawn messages arrive.
    ///
    /// WHY NetworkMessage INSTEAD OF [ClientRpc]:
    /// Mirror's [ClientRpc] attribute only works after the IL weaver has rewritten the
    /// method body to call SendRpcInternal and registered the dispatch delegate in a
    /// static constructor via RemoteProcedureCalls.RegisterRpc.  BepInEx plugin DLLs
    /// are NOT processed by Mirror's IL weaver, so [ClientRpc] decorated methods just
    /// execute locally — remote clients never receive anything.
    /// NetworkServer.SendToAll<T> / NetworkClient.RegisterHandler<T> bypass that
    /// pipeline entirely and work without IL weaving.
    [HarmonyPatch]
    static class NetworkManagerRegisterPrefabsPatch
    {
        private static bool _registered;

        static System.Reflection.MethodBase TargetMethod() =>
            AccessTools.Method(typeof(BNetworkManager), "OnStartClient");

        static void Postfix()
        {
            if (_registered)
                return;

            IssaPluginPlugin.Log.LogInfo(
                "[NetworkManager] Registering custom prefabs and message handlers."
            );

            // ── Prefab registration ──────────────────────────────────────────
            RegisterPrefabs();

            // ── NetworkMessage handlers ──────────────────────────────────────
            RegisterNetworkMessages();

            _registered = true;
            IssaPluginPlugin.Log.LogInfo(
                "[NetworkManager] Custom prefabs and message handlers registered."
            );
        }

        private static void RegisterNetworkMessages()
        {
            // -------------------------------
            // ---- FreezeEffect Messages ----
            NetworkClient.RegisterHandler<FreezeBeginMessage>(
                FreezeNetworkBridge.HandleFreezeBegin
            );
            Writer<FreezeBeginMessage>.write =
                FreezeBeginMessageSerialization.WriteFreezeBeginMessage;
            Reader<FreezeBeginMessage>.read =
                FreezeBeginMessageSerialization.ReadFreezeBeginMessage;

            NetworkClient.RegisterHandler<FreezeEndMessage>(FreezeNetworkBridge.HandleFreezeEnd);
            Writer<FreezeEndMessage>.write = FreezeEndMessageSerialization.WriteFreezeEndMessage;
            Reader<FreezeEndMessage>.read = FreezeEndMessageSerialization.ReadFreezeEndMessage;

            // -----------------------------
            // ---- LowGravity Messages ----
            NetworkClient.RegisterHandler<LowGravityBeginMessage>(
                LowGravityNetworkBridge.HandleLowGravityBegin
            );
            Writer<LowGravityBeginMessage>.write =
                LowGravityBeginMessageSerialization.WriteLowGravityBeginMessage;
            Reader<LowGravityBeginMessage>.read =
                LowGravityBeginMessageSerialization.ReadLowGravityBeginMessage;

            NetworkClient.RegisterHandler<LowGravityEndMessage>(
                LowGravityNetworkBridge.HandleLowGravityEnd
            );
            Writer<LowGravityEndMessage>.write =
                LowGravityEndMessageSerialization.WriteLowGravityEndMessage;
            Reader<LowGravityEndMessage>.read =
                LowGravityEndMessageSerialization.ReadLowGravityEndMessage;

            // --------------------------------
            // ---- StealthBomber Messages ----
            NetworkClient.RegisterHandler<BomberVisualSpawnMessage>(
                BomberNetworkBridge.HandleBomberVisualSpawn
            );
            Writer<BomberVisualSpawnMessage>.write =
                BomberVisualSpawnMessageSerialization.WriteBomberVisualSpawnMessage;
            Reader<BomberVisualSpawnMessage>.read =
                BomberVisualSpawnMessageSerialization.ReadBomberVisualSpawnMessage;

            NetworkClient.RegisterHandler<BomberShotDownMessage>(
                BomberNetworkBridge.HandleBomberShotDown
            );
            Writer<BomberShotDownMessage>.write =
                BomberShotDownMessageSerialization.WriteBomberShotDownMessage;
            Reader<BomberShotDownMessage>.read =
                BomberShotDownMessageSerialization.ReadBomberShotDownMessage;

            // ------------------------
            // ---- AC130 Messages ----
            NetworkClient.RegisterHandler<AC130SoundMessage>(AC130MessageHandlers.HandleAC130Sound);
            Writer<AC130SoundMessage>.write = AC130SoundMessageSerialization.WriteAC130SoundMessage;
            Reader<AC130SoundMessage>.read = AC130SoundMessageSerialization.ReadAC130SoundMessage;

            NetworkClient.RegisterHandler<AC130MaydayVfxMessage>(
                AC130MessageHandlers.HandleAC130MaydayVfx
            );
            Writer<AC130MaydayVfxMessage>.write =
                AC130MaydayVfxMessageSerialization.WriteAC130MaydayVfxMessage;
            Reader<AC130MaydayVfxMessage>.read =
                AC130MaydayVfxMessageSerialization.ReadAC130MaydayVfxMessage;

            NetworkClient.RegisterHandler<AC130MaydayImpactMessage>(
                AC130MessageHandlers.HandleAC130MaydayImpact
            );
            Writer<AC130MaydayImpactMessage>.write =
                AC130MaydayImpactMessageSerialization.WriteAC130MaydayImpactMessage;
            Reader<AC130MaydayImpactMessage>.read =
                AC130MaydayImpactMessageSerialization.ReadAC130MaydayImpactMessage;
        }

        private static void RegisterHandlers() { }

        private static void RegisterPrefabs()
        {
            RegisterPrefab(AssetLoader.DroppedCustomItemPrefab);
            RegisterPrefab(AssetLoader.BatModelPrefab);
            RegisterPrefab(AssetLoader.BomberPrefab);
            RegisterPrefab(AssetLoader.BomberProxyPrefab);
            RegisterPrefab(AssetLoader.AC130Prefab);
            RegisterPrefab(AssetLoader.BomberTabletPrefab);
            RegisterPrefab(AssetLoader.MissileTabletPrefab);
            RegisterPrefab(AssetLoader.Ac130TabletPrefab);
            RegisterPrefab(AssetLoader.FreezeModelPrefab);
            RegisterPrefab(AssetLoader.LowGravityModelPrefab);
            RegisterPrefab(AssetLoader.SniperRiflePrefab);
            RegisterPrefab(AssetLoader.BloodSplatterPrefab);
        }

        private static void RegisterPrefab(GameObject prefab)
        {
            if (prefab == null)
                return;

            var ni = prefab.GetComponent<NetworkIdentity>();
            if (ni == null)
            {
                IssaPluginPlugin.Log.LogWarning(
                    $"[NetworkManager] Skipping {prefab.name}: no NetworkIdentity."
                );
                return;
            }

            if (ni.assetId == 0)
            {
                IssaPluginPlugin.Log.LogWarning(
                    $"[NetworkManager] Skipping {prefab.name}: assetId is 0 (not stable)."
                );
                return;
            }

            NetworkClient.RegisterPrefab(prefab);
            IssaPluginPlugin.Log.LogInfo(
                $"[NetworkManager] Registered '{prefab.name}' assetId={ni.assetId}."
            );
        }

        // Registration delegates point into AC130MessageHandlers below.
    }

    /// AC130 NetworkMessage handlers — kept in a separate (non-patch) class so
    /// the Harmony analyser does not misidentify the 'msg' parameters as patch
    /// parameters and emit false Harmony003 warnings.
    static class AC130MessageHandlers
    {
        internal static void HandleAC130Sound(AC130SoundMessage msg)
        {
            var clip = AssetLoader.AC130AboveClip;
            if (clip == null)
            {
                IssaPluginPlugin.Log.LogWarning("[AC130] Audio clip not loaded.");
                return;
            }

            var go = new GameObject("AC130_Sound");
            var src = go.AddComponent<AudioSource>();
            src.clip = clip;
            src.spatialBlend = 0f;
            src.volume = 1f;
            src.Play();
            Object.Destroy(go, clip.length + 0.1f);
        }

        internal static void HandleAC130MaydayVfx(AC130MaydayVfxMessage msg)
        {
            // Skip for the owning client — TargetBeginMayday handles the cockpit path.
            // All other clients get the external smoke/fire mayday behaviour here.
            var localBridge = NetworkClient.localPlayer?.GetComponent<AC130NetworkBridge>();
            if (localBridge != null && localBridge.LocalSessionActive)
                return;

            if (!NetworkClient.spawned.TryGetValue(msg.GunshipNetId, out var ni) || ni == null)
                return;

            var gunship = ni.gameObject;
            if (gunship.GetComponent<AC130MaydayBehaviour>() == null)
            {
                var mayday = gunship.AddComponent<AC130MaydayBehaviour>();
                mayday.IsLocalPlayer = false;
                mayday.MapCentre =
                    gunship.GetComponent<AC130FlyBehaviour>()?.mapCentre ?? Vector3.zero;
            }
        }

        internal static void HandleAC130MaydayImpact(AC130MaydayImpactMessage msg)
        {
            float duration = Configuration.AC130MaydayExplosionDuration.Value;

            if (AssetLoader.MaydayExplosionVfxPrefab != null)
            {
                var vfxGo = Object.Instantiate(
                    AssetLoader.MaydayExplosionVfxPrefab,
                    msg.ImpactPos,
                    Quaternion.identity
                );
                Object.Destroy(vfxGo, duration);
            }
            else
            {
                VfxManager.PlayPooledVfxLocalOnly(
                    VfxType.RocketLauncherRocketExplosion,
                    msg.ImpactPos,
                    Quaternion.identity,
                    Vector3.one * Configuration.AC130MaydayExplosionScale.Value
                );
            }

            if (AssetLoader.AC130ImpactVfxPrefab != null)
            {
                var debrisGo = Object.Instantiate(
                    AssetLoader.AC130ImpactVfxPrefab,
                    msg.ImpactPos,
                    Quaternion.identity
                );
                Object.Destroy(debrisGo, duration);
            }

            CameraModuleController.Shake(
                GameManager.CameraGameplaySettings.RocketExplosionScreenshakeSettings,
                msg.ImpactPos
            );
        }
    }
}
