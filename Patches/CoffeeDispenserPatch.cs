using System.Collections;
using System.Reflection;
using HarmonyLib;
using IssaPlugin.Items;
using Mirror;
using UnityEngine;

namespace IssaPlugin.Patches
{
    /// <summary>
    /// With a configurable probability, replaces the coffee can that falls from a
    /// coffee dispenser with a Red Bull can (visual + item-type).
    ///
    /// Flow:
    ///   Server: PhysicalItem.OnStartServer fires when the coffee can is spawned.
    ///           We roll the dice, change itemType to RedBull so the player receives
    ///           a Red Bull when they pick it up, then schedule a message to all
    ///           clients so they also see the Red Bull can model.
    ///
    ///   Client: CoffeeDispenserRedBullMessage arrives one frame after the spawn
    ///           message, so the object is already in NetworkClient.spawned.
    ///           We hide the coffee can mesh, parent the Red Bull model as a visual
    ///           child, and fix the interaction-prompt item-type field.
    ///
    /// Why OnStartServer instead of UserCode_CmdGiveToPlayer:
    ///   Patching at pick-up time adds RedBull to inventory but leaves a coffee can
    ///   on the ground.  Patching at spawn time lets the correct visual fall out of
    ///   the machine, sit on the ground, and then be picked up naturally.
    /// </summary>
    [HarmonyPatch]
    static class CoffeeDispenserRedBullPatch
    {
        private static readonly FieldInfo ItemTypeField =
            AccessTools.Field(typeof(PhysicalItem), "itemType");

        static MethodBase TargetMethod() =>
            AccessTools.Method(typeof(PhysicalItem), "OnStartServer");

        static void Postfix(PhysicalItem __instance)
        {
            if ((ItemType)ItemTypeField.GetValue(__instance) != ItemType.Coffee)
                return;
            if (!(Random.value < Configuration.RedBullDispenserChance.Value))
                return;

            // Server: change what the player receives on pick-up.
            ItemTypeField.SetValue(__instance, ItemRegistry.RedBullItemType);

            // Delay one frame so Mirror's spawn message reaches clients before we
            // reference the object by netId in our visual-swap message.
            __instance.StartCoroutine(BroadcastVisualSwap(__instance.netId));

            IssaPluginPlugin.Log.LogInfo("[CoffeeDispenser] Red Bull can substituted.");
        }

        private static IEnumerator BroadcastVisualSwap(uint netId)
        {
            yield return null;
            NetworkServer.SendToAll(new CoffeeDispenserRedBullMessage { NetId = netId });
        }

    }

    /// <summary>
    /// Client-side handler for CoffeeDispenserRedBullMessage.
    /// Kept in a separate non-patch class so Harmony does not analyse HandleVisualSwap
    /// as a potential patch method and emit false Harmony003 warnings.
    /// </summary>
    static class CoffeeDispenserClientHandlers
    {
        private static readonly FieldInfo ItemTypeField =
            AccessTools.Field(typeof(PhysicalItem), "itemType");

        public static void HandleVisualSwap(CoffeeDispenserRedBullMessage msg)
        {
            if (!NetworkClient.spawned.TryGetValue(msg.NetId, out var identity))
            {
                IssaPluginPlugin.Log.LogWarning(
                    $"[CoffeeDispenser] Visual swap: object {msg.NetId} not found on client."
                );
                return;
            }

            var physicalItem = identity.GetComponent<PhysicalItem>();
            if (physicalItem == null)
                return;

            // Fix the interaction prompt: clients read itemType for the "pick up X" string.
            ItemTypeField.SetValue(physicalItem, ItemRegistry.RedBullItemType);

            // Hide the coffee can mesh renderers.
            foreach (var r in physicalItem.GetComponentsInChildren<MeshRenderer>())
                r.enabled = false;

            // Parent the Red Bull can model as a local visual child.
            if (AssetLoader.RedBullHandheldPrefab == null)
                return;

            var visual = Object.Instantiate(AssetLoader.RedBullHandheldPrefab);
            visual.transform.SetParent(physicalItem.transform, false);
            visual.transform.localPosition = Vector3.zero;
            visual.transform.localRotation = Quaternion.identity;
        }
    }
}
