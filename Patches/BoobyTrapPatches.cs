using System.Reflection;
using HarmonyLib;
using IssaPlugin.Items;
using Mirror;
using UnityEngine;

namespace IssaPlugin.Patches
{
    // ====================================================================
    //  Shared helper
    // ====================================================================

    internal static class BoobyTrapPatchHelpers
    {
        /// Looks up the PlayerInfo for a trapper by their network identity netId.
        /// Returns null if the trapper disconnected or the netId is 0.
        internal static PlayerInfo GetTrapperInfo(uint trapperNetId)
        {
            if (trapperNetId == 0)
                return null;
            if (!NetworkServer.spawned.TryGetValue(trapperNetId, out var ni))
                return null;
            return ni.GetComponent<PlayerInfo>();
        }
    }

    // ====================================================================
    //  Patch 1 — Item box booby trap
    //
    //  Intercepts ItemSpawner.OnTriggerEnter on the server.  When a player
    //  walks into a trapped item box the trap consumes, an explosion fires
    //  at the player's position, and the original handler is skipped so the
    //  box stays intact (the game's respawn timer continues normally).
    //
    //  Edge case: if two players enter simultaneously the first triggers the
    //  trap; the second runs the unpatched path and gets the item normally.
    // ====================================================================

    [HarmonyPatch]
    static class BoobyTrapItemBoxPatch
    {
        static MethodBase TargetMethod() =>
            AccessTools.Method(typeof(ItemSpawner), "OnTriggerEnter");

        static bool Prefix(ItemSpawner __instance, Collider other)
        {
            if (!NetworkServer.active)
                return true;

            Entity entity;
            if (!other.TryGetComponentInParent(out entity, true))
                return true;

            if (!entity.IsPlayer)
                return true;

            var playerInfo = entity.PlayerInfo;
            uint playerNetId = playerInfo.netId;

            if (
                !BoobyTrapNetworkBridge.TryConsumeItemBoxTrap(
                    __instance.netId,
                    playerNetId,
                    out uint trapperNetId
                )
            )
                return true;

            var trapperInfo = BoobyTrapPatchHelpers.GetTrapperInfo(trapperNetId);
            BoobyTrapNetworkBridge.ServerTriggerDetonation(
                playerInfo.transform.position,
                trapperInfo
            );

            return false; // skip base — box remains intact, respawn timer continues
        }
    }

    // ====================================================================
    //  Patch 2 — Golf cart booby trap
    //
    //  ServerTryAssignPassengerToSeat is the single server-side entry point
    //  for all cart entry paths (direct enter and reserved seat).  It is a
    //  private method, so TargetMethod() uses AccessTools.
    // ====================================================================

    [HarmonyPatch]
    static class BoobyTrapGolfCartPatch
    {
        // Dictionary<PlayerInfo, int> — passenger → seat index.
        private static readonly FieldInfo PassengerIndicesField =
            AccessTools.Field(typeof(GolfCartInfo), "passengerIndices");

        static MethodBase TargetMethod() =>
            AccessTools.Method(typeof(GolfCartInfo), "ServerTryAssignPassengerToSeat");

        static bool Prefix(GolfCartInfo __instance, PlayerInfo passenger, ref bool __result)
        {
            if (!NetworkServer.active)
                return true;

            // ServerTryAssignPassengerToSeat is also called for seat changes within the
            // cart (CmdTryChangeLocalPlayerSeat).  Only trigger the trap on first entry —
            // skip if the passenger is already aboard.
            if (
                PassengerIndicesField?.GetValue(__instance)
                    is System.Collections.IDictionary passengerIndices
                && passengerIndices.Contains(passenger)
            )
                return true;

            uint playerNetId = passenger.netId;

            if (
                !BoobyTrapNetworkBridge.TryConsumeGolfCartTrap(
                    __instance.netId,
                    playerNetId,
                    out uint trapperNetId
                )
            )
                return true;

            var trapperInfo = BoobyTrapPatchHelpers.GetTrapperInfo(trapperNetId);
            BoobyTrapNetworkBridge.ServerTriggerDetonation(
                passenger.transform.position,
                trapperInfo
            );

            __result = false;
            return false; // skip base — cart remains unoccupied
        }
    }

    // ====================================================================
    //  Patch 3 — Inventory item booby trap (client-side intercept)
    //
    //  TryUseItem is local-player-only, so this runs wherever the victim is
    //  playing.  The victim's client was already notified by the server via
    //  BoobyTrapSlotMarkedMessage when the trap was set.  On match, we abort
    //  the use and send BoobyTrapInventoryTriggeredMessage so the server can
    //  validate and detonate.
    //
    //  Priority.High fires this before TryUseItemPatch (Priority.Normal),
    //  ensuring the item effect never triggers.
    // ====================================================================

    [HarmonyPatch]
    [HarmonyPriority(Priority.High)]
    static class BoobyTrapInventoryUsePatch
    {
        static MethodBase TargetMethod() =>
            AccessTools.Method(typeof(PlayerInventory), "TryUseItem");

        static bool Prefix(PlayerInventory __instance, ref bool shouldEatInput, ref bool __result)
        {
            if (!__instance.isLocalPlayer)
                return true;

            int slot = __instance.EquippedItemIndex;

            if (!BoobyTrapNetworkBridge.IsLocalSlotTrapped(slot))
                return true;

            // Remove from local cache immediately so a second press doesn't
            // double-send while the server round-trip is in flight.
            BoobyTrapNetworkBridge.ClearLocalTrappedSlot(slot);

            NetworkClient.Send(new BoobyTrapInventoryTriggeredMessage { SlotIndex = slot });

            shouldEatInput = true;
            __result = false;
            return false; // abort — server will detonate
        }
    }
}
