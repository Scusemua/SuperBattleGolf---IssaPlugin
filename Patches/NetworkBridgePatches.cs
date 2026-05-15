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
            if (!__instance.GetComponent<LowGravityNetworkBridge>())
                __instance.gameObject.AddComponent<LowGravityNetworkBridge>();
            if (!__instance.GetComponent<DonutNetworkBridge>())
                __instance.gameObject.AddComponent<DonutNetworkBridge>();
            if (!__instance.GetComponent<JavelinNetworkBridge>())
                __instance.gameObject.AddComponent<JavelinNetworkBridge>();
            if (!__instance.GetComponent<StickyGrenadeNetworkBridge>())
                __instance.gameObject.AddComponent<StickyGrenadeNetworkBridge>();
            if (!__instance.GetComponent<BearNetworkBridge>())
                __instance.gameObject.AddComponent<BearNetworkBridge>();
            if (!__instance.GetComponent<NukeNetworkBridge>())
                __instance.gameObject.AddComponent<NukeNetworkBridge>();
            if (!__instance.GetComponent<BlackHoleGrenadeNetworkBridge>())
                __instance.gameObject.AddComponent<BlackHoleGrenadeNetworkBridge>();
            if (!__instance.GetComponent<PlaceableWallNetworkBridge>())
                __instance.gameObject.AddComponent<PlaceableWallNetworkBridge>();
            if (!__instance.GetComponent<HarrierNetworkBridge>())
                __instance.gameObject.AddComponent<HarrierNetworkBridge>();
            if (!__instance.GetComponent<PositionSwapNetworkBridge>())
                __instance.gameObject.AddComponent<PositionSwapNetworkBridge>();
            if (!__instance.GetComponent<PoisonJarNetworkBridge>())
                __instance.gameObject.AddComponent<PoisonJarNetworkBridge>();
            if (!__instance.GetComponent<DroneSwarmNetworkBridge>())
                __instance.gameObject.AddComponent<DroneSwarmNetworkBridge>();
            if (!__instance.GetComponent<RedBullNetworkBridge>())
                __instance.gameObject.AddComponent<RedBullNetworkBridge>();
            if (!__instance.GetComponent<SuperDonutNetworkBridge>())
                __instance.gameObject.AddComponent<SuperDonutNetworkBridge>();
            if (!__instance.GetComponent<GravityGunNetworkBridge>())
                __instance.gameObject.AddComponent<GravityGunNetworkBridge>();
            if (!__instance.GetComponent<RocketTetherNetworkBridge>())
                __instance.gameObject.AddComponent<RocketTetherNetworkBridge>();
            if (!__instance.GetComponent<JetpackNetworkBridge>())
                __instance.gameObject.AddComponent<JetpackNetworkBridge>();
            if (!__instance.GetComponent<TeleporterNetworkBridge>())
                __instance.gameObject.AddComponent<TeleporterNetworkBridge>();
            if (!__instance.GetComponent<SpinachNetworkBridge>())
                __instance.gameObject.AddComponent<SpinachNetworkBridge>();
            if (!__instance.GetComponent<FlamethrowerNetworkBridge>())
                __instance.gameObject.AddComponent<FlamethrowerNetworkBridge>();
            if (!__instance.GetComponent<RocketTetherGrenadeNetworkBridge>())
                __instance.gameObject.AddComponent<RocketTetherGrenadeNetworkBridge>();
            if (!__instance.GetComponent<WindStormNetworkBridge>())
                __instance.gameObject.AddComponent<WindStormNetworkBridge>();
            if (!__instance.GetComponent<HunterDroneNetworkBridge>())
                __instance.gameObject.AddComponent<HunterDroneNetworkBridge>();
            if (!__instance.GetComponent<UfoAbductionNetworkBridge>())
                __instance.gameObject.AddComponent<UfoAbductionNetworkBridge>();
            if (!__instance.GetComponent<MoonNetworkBridge>())
                __instance.gameObject.AddComponent<MoonNetworkBridge>();
            if (!__instance.GetComponent<ShapeShifterNetworkBridge>())
                __instance.gameObject.AddComponent<ShapeShifterNetworkBridge>();
            if (!__instance.GetComponent<SuperShapeShifterNetworkBridge>())
                __instance.gameObject.AddComponent<SuperShapeShifterNetworkBridge>();
            if (!__instance.GetComponent<ExplosiveGolfBallsNetworkBridge>())
                __instance.gameObject.AddComponent<ExplosiveGolfBallsNetworkBridge>();

            IssaPluginPlugin.Log.LogDebug(
                "[Network] Bridge components injected onto player object."
            );
        }
    }
}
