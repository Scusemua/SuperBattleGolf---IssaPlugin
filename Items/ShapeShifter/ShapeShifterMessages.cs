using Mirror;

namespace IssaPlugin.Items
{
    // ── Client → Server ──────────────────────────────────────────────────────

    /// Sent by the local client when the player selects a target from the
    /// ShapeShifterOverlay chooser panel.
    public struct ShapeShifterRequestMessage : NetworkMessage
    {
        public uint TargetNetId;

        /// The client's local EquippedItemIndex at the moment of use.
        /// Passed so the server can validate the correct slot without relying on
        /// NetworkedEquippedItemIndex (not synced client → server).
        public int EquippedSlotIndex;
    }

    public static class ShapeShifterRequestMessageSerialization
    {
        public static void WriteShapeShifterRequestMessage(
            NetworkWriter writer,
            ShapeShifterRequestMessage msg
        )
        {
            writer.WriteUInt(msg.TargetNetId);
            writer.WriteInt(msg.EquippedSlotIndex);
        }

        public static ShapeShifterRequestMessage ReadShapeShifterRequestMessage(
            NetworkReader reader
        )
        {
            return new ShapeShifterRequestMessage
            {
                TargetNetId = reader.ReadUInt(),
                EquippedSlotIndex = reader.ReadInt(),
            };
        }
    }

    /// Sent by the local client when the Super Shape Shifter item is used
    /// (no target selection needed — affects every other player).
    public struct SuperShapeShifterRequestMessage : NetworkMessage
    {
        public int EquippedSlotIndex;
    }

    public static class SuperShapeShifterRequestMessageSerialization
    {
        public static void WriteSuperShapeShifterRequestMessage(
            NetworkWriter writer,
            SuperShapeShifterRequestMessage msg
        )
        {
            writer.WriteInt(msg.EquippedSlotIndex);
        }

        public static SuperShapeShifterRequestMessage ReadSuperShapeShifterRequestMessage(
            NetworkReader reader
        )
        {
            return new SuperShapeShifterRequestMessage { EquippedSlotIndex = reader.ReadInt() };
        }
    }

    // ── Server → All Clients ─────────────────────────────────────────────────

    /// Broadcast by the server when a player's ball begins (or has its duration
    /// extended for) the cube effect. Shared by both ShapeShifter and SuperShapeShifter.
    /// Duration is the remaining time in seconds so clients can drive a timer bar.
    public struct ShapeShifterBeginMessage : NetworkMessage
    {
        public uint TargetNetId;
        public float Duration;

        /// When true the client should re-randomize the shape even if the effect
        /// is already active (used by Super Shape Shifter re-use).
        public bool Reroll;
    }

    public static class ShapeShifterBeginMessageSerialization
    {
        public static void WriteShapeShifterBeginMessage(
            NetworkWriter writer,
            ShapeShifterBeginMessage msg
        )
        {
            writer.WriteUInt(msg.TargetNetId);
            writer.WriteFloat(msg.Duration);
            writer.WriteBool(msg.Reroll);
        }

        public static ShapeShifterBeginMessage ReadShapeShifterBeginMessage(NetworkReader reader)
        {
            return new ShapeShifterBeginMessage
            {
                TargetNetId = reader.ReadUInt(),
                Duration = reader.ReadFloat(),
                Reroll = reader.ReadBool(),
            };
        }
    }

    /// Broadcast by the server when the cube effect on a player's ball ends.
    public struct ShapeShifterEndMessage : NetworkMessage
    {
        public uint TargetNetId;
    }

    public static class ShapeShifterEndMessageSerialization
    {
        public static void WriteShapeShifterEndMessage(
            NetworkWriter writer,
            ShapeShifterEndMessage msg
        )
        {
            writer.WriteUInt(msg.TargetNetId);
        }

        public static ShapeShifterEndMessage ReadShapeShifterEndMessage(NetworkReader reader)
        {
            return new ShapeShifterEndMessage { TargetNetId = reader.ReadUInt() };
        }
    }
}
