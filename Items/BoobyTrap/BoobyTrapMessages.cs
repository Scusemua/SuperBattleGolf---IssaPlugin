using Mirror;
using UnityEngine;

namespace IssaPlugin.Items
{
    // ── Client → Server ──────────────────────────────────────────────────────

    /// Sent when the player uses the Booby Trap while a world target (item box
    /// or golf cart) is selected in the overlay.
    public struct BoobyTrapPlaceMessage : NetworkMessage
    {
        /// Indicates whether the target is an ItemBox or a GolfCart.
        public BoobyTrapTargetType TargetType;
        /// Mirror netId of the ItemSpawner or GolfCartInfo component.
        public uint TargetNetId;
    }

    public static class BoobyTrapPlaceMessageSerialization
    {
        public static void WriteBoobyTrapPlaceMessage(NetworkWriter w, BoobyTrapPlaceMessage m)
        {
            w.WriteByte((byte)m.TargetType);
            w.WriteUInt(m.TargetNetId);
        }

        public static BoobyTrapPlaceMessage ReadBoobyTrapPlaceMessage(NetworkReader r) =>
            new BoobyTrapPlaceMessage
            {
                TargetType = (BoobyTrapTargetType)r.ReadByte(),
                TargetNetId = r.ReadUInt(),
            };
    }

    /// Sent when the player uses the Booby Trap in inventory-item mode to
    /// drop one of their held items as a booby trap.
    public struct BoobyTrapDropItemMessage : NetworkMessage
    {
        /// Inventory slot index of the item to drop.
        public int SourceSlotIndex;
    }

    public static class BoobyTrapDropItemMessageSerialization
    {
        public static void WriteBoobyTrapDropItemMessage(NetworkWriter w, BoobyTrapDropItemMessage m)
        {
            w.WriteInt(m.SourceSlotIndex);
        }

        public static BoobyTrapDropItemMessage ReadBoobyTrapDropItemMessage(NetworkReader r) =>
            new BoobyTrapDropItemMessage { SourceSlotIndex = r.ReadInt() };
    }

    // ── Server → Trapper Client only ─────────────────────────────────────────

    /// Sent only to the trapper's connection after a world-object trap is placed,
    /// so the trapper can spawn a local-only VFX indicator on the trapped object.
    public struct BoobyTrapPlacedMessage : NetworkMessage
    {
        public BoobyTrapTargetType TargetType;
        public uint TargetNetId;
    }

    public static class BoobyTrapPlacedMessageSerialization
    {
        public static void WriteBoobyTrapPlacedMessage(NetworkWriter w, BoobyTrapPlacedMessage m)
        {
            w.WriteByte((byte)m.TargetType);
            w.WriteUInt(m.TargetNetId);
        }

        public static BoobyTrapPlacedMessage ReadBoobyTrapPlacedMessage(NetworkReader r) =>
            new BoobyTrapPlacedMessage
            {
                TargetType = (BoobyTrapTargetType)r.ReadByte(),
                TargetNetId = r.ReadUInt(),
            };
    }

    // ── Server → All Clients ─────────────────────────────────────────────────

    /// Broadcast when any booby trap detonates.  Clients use this to spawn
    /// explosion VFX and apply TryKnockOut on the local player if affected.
    public struct BoobyTrapExplosionMessage : NetworkMessage
    {
        public Vector3 Position;
        /// The player who originally placed the trap (for kill-feed credit).
        public PlayerInfo TrapperInfo;
        public ItemUseId ItemUseId;
    }

    public static class BoobyTrapExplosionMessageSerialization
    {
        public static void WriteBoobyTrapExplosionMessage(
            NetworkWriter w,
            BoobyTrapExplosionMessage m
        )
        {
            w.WriteVector3(m.Position);
            w.WriteNetworkBehaviour(m.TrapperInfo);
            w.Write(m.ItemUseId);
        }

        public static BoobyTrapExplosionMessage ReadBoobyTrapExplosionMessage(NetworkReader r) =>
            new BoobyTrapExplosionMessage
            {
                Position = r.ReadVector3(),
                TrapperInfo = r.ReadNetworkBehaviour<PlayerInfo>(),
                ItemUseId = r.Read<ItemUseId>(),
            };
    }

    // ── Server → Victim Client only ──────────────────────────────────────────

    /// Sent to the victim's connection when their inventory slot is marked as
    /// booby-trapped, so the client can intercept the use locally before the
    /// item effect fires.
    public struct BoobyTrapSlotMarkedMessage : NetworkMessage
    {
        public int SlotIndex;
    }

    public static class BoobyTrapSlotMarkedMessageSerialization
    {
        public static void WriteBoobyTrapSlotMarkedMessage(
            NetworkWriter w,
            BoobyTrapSlotMarkedMessage m
        ) => w.WriteInt(m.SlotIndex);

        public static BoobyTrapSlotMarkedMessage ReadBoobyTrapSlotMarkedMessage(
            NetworkReader r
        ) => new BoobyTrapSlotMarkedMessage { SlotIndex = r.ReadInt() };
    }

    // ── Client → Server (inventory trigger) ──────────────────────────────────

    /// Sent by the victim's client when they attempt to use a slot that their
    /// local cache knows is booby-trapped.  The server validates against the
    /// authoritative dict before detonating.
    public struct BoobyTrapInventoryTriggeredMessage : NetworkMessage
    {
        public int SlotIndex;
    }

    public static class BoobyTrapInventoryTriggeredMessageSerialization
    {
        public static void WriteBoobyTrapInventoryTriggeredMessage(
            NetworkWriter w,
            BoobyTrapInventoryTriggeredMessage m
        ) => w.WriteInt(m.SlotIndex);

        public static BoobyTrapInventoryTriggeredMessage ReadBoobyTrapInventoryTriggeredMessage(
            NetworkReader r
        ) => new BoobyTrapInventoryTriggeredMessage { SlotIndex = r.ReadInt() };
    }

    // ── Shared enum ──────────────────────────────────────────────────────────

    public enum BoobyTrapTargetType : byte
    {
        ItemBox = 0,
        GolfCart = 1,
    }
}
