using Mirror;

namespace IssaPlugin.Items
{
    // ── Client → Server ──────────────────────────────────────────────────────

    /// Sent by the local client when the player selects a target from the
    /// CubeBallOverlay chooser panel.
    public struct CubeBallRequestMessage : NetworkMessage
    {
        public uint TargetNetId;

        /// The client's local EquippedItemIndex at the moment of use.
        /// Passed so the server can validate the correct slot without relying on
        /// NetworkedEquippedItemIndex (not synced client → server).
        public int EquippedSlotIndex;
    }

    public static class CubeBallRequestMessageSerialization
    {
        public static void WriteCubeBallRequestMessage(
            NetworkWriter writer,
            CubeBallRequestMessage msg
        )
        {
            writer.WriteUInt(msg.TargetNetId);
            writer.WriteInt(msg.EquippedSlotIndex);
        }

        public static CubeBallRequestMessage ReadCubeBallRequestMessage(NetworkReader reader)
        {
            return new CubeBallRequestMessage
            {
                TargetNetId = reader.ReadUInt(),
                EquippedSlotIndex = reader.ReadInt(),
            };
        }
    }

    /// Sent by the local client when the Super Cube Ball item is used
    /// (no target selection needed — affects every other player).
    public struct SuperCubeBallRequestMessage : NetworkMessage
    {
        public int EquippedSlotIndex;
    }

    public static class SuperCubeBallRequestMessageSerialization
    {
        public static void WriteSuperCubeBallRequestMessage(
            NetworkWriter writer,
            SuperCubeBallRequestMessage msg
        )
        {
            writer.WriteInt(msg.EquippedSlotIndex);
        }

        public static SuperCubeBallRequestMessage ReadSuperCubeBallRequestMessage(
            NetworkReader reader
        )
        {
            return new SuperCubeBallRequestMessage { EquippedSlotIndex = reader.ReadInt() };
        }
    }

    // ── Server → All Clients ─────────────────────────────────────────────────

    /// Broadcast by the server when a player's ball begins (or has its duration
    /// extended for) the cube effect. Shared by both CubeBall and SuperCubeBall.
    /// Duration is the remaining time in seconds so clients can drive a timer bar.
    public struct CubeBallBeginMessage : NetworkMessage
    {
        public uint TargetNetId;
        public float Duration;
    }

    public static class CubeBallBeginMessageSerialization
    {
        public static void WriteCubeBallBeginMessage(NetworkWriter writer, CubeBallBeginMessage msg)
        {
            writer.WriteUInt(msg.TargetNetId);
            writer.WriteFloat(msg.Duration);
        }

        public static CubeBallBeginMessage ReadCubeBallBeginMessage(NetworkReader reader)
        {
            return new CubeBallBeginMessage
            {
                TargetNetId = reader.ReadUInt(),
                Duration = reader.ReadFloat(),
            };
        }
    }

    /// Broadcast by the server when the cube effect on a player's ball ends.
    public struct CubeBallEndMessage : NetworkMessage
    {
        public uint TargetNetId;
    }

    public static class CubeBallEndMessageSerialization
    {
        public static void WriteCubeBallEndMessage(NetworkWriter writer, CubeBallEndMessage msg)
        {
            writer.WriteUInt(msg.TargetNetId);
        }

        public static CubeBallEndMessage ReadCubeBallEndMessage(NetworkReader reader)
        {
            return new CubeBallEndMessage { TargetNetId = reader.ReadUInt() };
        }
    }
}
