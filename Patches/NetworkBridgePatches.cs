using HarmonyLib;
using IssaPlugin.Items;
using Mirror;
using UnityEngine;

namespace IssaPlugin.Patches
{
    /// Injects our NetworkBehaviour bridge components onto every player object
    /// before Mirror's NetworkIdentity.Awake() discovers them.
    /// Both server and client run this patch, so component indices stay in sync.
    [HarmonyPatch(typeof(NetworkIdentity), "Awake")]
    static class AddBridgeComponentsPatch
    {
        static void Prefix(NetworkIdentity __instance)
        {
            if (__instance.GetComponent<PlayerInventory>() == null)
                return;

            if (!__instance.GetComponent<BomberNetworkBridge>())
                __instance.gameObject.AddComponent<BomberNetworkBridge>();
            if (!__instance.GetComponent<MissileNetworkBridge>())
                __instance.gameObject.AddComponent<MissileNetworkBridge>();
            if (!__instance.GetComponent<AC130NetworkBridge>())
                __instance.gameObject.AddComponent<AC130NetworkBridge>();
            if (!__instance.GetComponent<FreezeNetworkBridge>())
                __instance.gameObject.AddComponent<FreezeNetworkBridge>();

            IssaPluginPlugin.Log.LogInfo(
                "[Network] Bridge components injected onto player object."
            );
        }
    }
}
