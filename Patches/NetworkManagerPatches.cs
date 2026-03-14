using HarmonyLib;
using IssaPlugin.Items;
using Mirror;
using UnityEngine;

namespace IssaPlugin.Patches
{
    /// Registers all custom networked prefabs with Mirror's NetworkClient
    /// immediately after the client transport connects.
    ///
    /// Hook point: BNetworkManager.OnStartClient() — fires on every client
    /// (including the listen-server host).  Prefabs must be registered before
    /// the server sends any SpawnMessage for them, so this early hook is ideal.
    ///
    /// Prefabs that must be registered:
    ///   • DroppedCustomItemPrefab — has a baked assetId from the asset bundle
    ///                               (error "Failed to spawn … assetId=2784332744")
    ///   • BomberProxyPrefab       — NetworkIdentity ensured in AssetLoader.Load()
    ///   • AC130Prefab             — NetworkIdentity + NetworkTransform added in
    ///                               AssetLoader.Load() with stable assetId
    [HarmonyPatch]
    static class NetworkManagerRegisterPrefabsPatch
    {
        static System.Reflection.MethodBase TargetMethod() =>
            AccessTools.Method(typeof(BNetworkManager), "OnStartClient");

        static void Postfix()
        {
            RegisterPrefab(AssetLoader.DroppedCustomItemPrefab);
            RegisterPrefab(AssetLoader.BomberProxyPrefab);
            RegisterPrefab(AssetLoader.AC130Prefab);

            IssaPluginPlugin.Log.LogInfo("[NetworkManager] Custom prefab registration complete.");
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
    }
}
