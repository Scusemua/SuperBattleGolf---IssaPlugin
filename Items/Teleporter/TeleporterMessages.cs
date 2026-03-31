using Mirror;
using UnityEngine;

namespace IssaPlugin.Items
{
    // ── Client → Server ──────────────────────────────────────────────────────

    /// <summary>
    /// Sent by the local client after the player has confirmed a teleport destination
    /// in the targeting UI. The server validates, consumes the item, moves the player,
    /// and broadcasts VFX to all clients.
    /// </summary>
    public struct TeleporterUseMessage : NetworkMessage
    {
        /// World-space destination the local player chose.
        public Vector3 Destination;

        /// Inventory slot index of the equipped Teleporter (used for item consumption).
        public int EquippedIndex;
    }

    public static class TeleporterUseMessageSerialization
    {
        public static void WriteTeleporterUseMessage(NetworkWriter writer, TeleporterUseMessage msg)
        {
            writer.WriteVector3(msg.Destination);
            writer.WriteInt(msg.EquippedIndex);
        }

        public static TeleporterUseMessage ReadTeleporterUseMessage(NetworkReader reader)
        {
            return new TeleporterUseMessage
            {
                Destination = reader.ReadVector3(),
                EquippedIndex = reader.ReadInt(),
            };
        }
    }

    // ── Server → Specific Client ─────────────────────────────────────────────

    /// <summary>
    /// Sent only to the using client's connection so it warps the local
    /// CharacterController / Rigidbody to the chosen destination.
    /// (Mirrors the pattern used by PositionSwapTeleportMessage.)
    /// </summary>
    public struct TeleporterWarpMessage : NetworkMessage
    {
        public Vector3 Destination;
    }

    public static class TeleporterWarpMessageSerialization
    {
        public static void WriteTeleporterWarpMessage(
            NetworkWriter writer,
            TeleporterWarpMessage msg
        )
        {
            writer.WriteVector3(msg.Destination);
        }

        public static TeleporterWarpMessage ReadTeleporterWarpMessage(NetworkReader reader)
        {
            return new TeleporterWarpMessage { Destination = reader.ReadVector3() };
        }
    }

    // ── Server → All Clients ─────────────────────────────────────────────────

    /// <summary>
    /// Broadcast to every client so they can play the teleport VFX particle effect at
    /// both the origin and the destination.
    /// </summary>
    public struct TeleporterVfxMessage : NetworkMessage
    {
        /// World-space position where the player was standing before teleporting.
        public Vector3 Origin;

        /// World-space position where the player lands after teleporting.
        public Vector3 Destination;
    }

    public static class TeleporterVfxMessageSerialization
    {
        public static void WriteTeleporterVfxMessage(NetworkWriter writer, TeleporterVfxMessage msg)
        {
            writer.WriteVector3(msg.Origin);
            writer.WriteVector3(msg.Destination);
        }

        public static TeleporterVfxMessage ReadTeleporterVfxMessage(NetworkReader reader)
        {
            return new TeleporterVfxMessage
            {
                Origin = reader.ReadVector3(),
                Destination = reader.ReadVector3(),
            };
        }
    }
}
