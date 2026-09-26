using Mirror;

namespace IssaPlugin.Items
{
    public struct OrbBomberRequestMessage : NetworkMessage
    {
        public uint TargetNetId;
        public int EquippedSlotIndex;
    }

    public static class OrbBomberRequestMessageSerialization
    {
        public static void WriteOrbBomberRequestMessage(
            NetworkWriter writer,
            OrbBomberRequestMessage msg
        )
        {
            writer.WriteUInt(msg.TargetNetId);
            writer.WriteInt(msg.EquippedSlotIndex);
        }

        public static OrbBomberRequestMessage ReadOrbBomberRequestMessage(NetworkReader reader)
        {
            return new OrbBomberRequestMessage
            {
                TargetNetId = reader.ReadUInt(),
                EquippedSlotIndex = reader.ReadInt(),
            };
        }
    }

    public struct OrbBomberSequenceStartMessage : NetworkMessage
    {
        public uint OrbNetId;
        public float Duration;
        public int FlashCount;
        public float SizeMultiplier;
    }

    public static class OrbBomberSequenceStartMessageSerialization
    {
        public static void WriteOrbBomberSequenceStartMessage(
            NetworkWriter writer,
            OrbBomberSequenceStartMessage msg
        )
        {
            writer.WriteUInt(msg.OrbNetId);
            writer.WriteFloat(msg.Duration);
            writer.WriteInt(msg.FlashCount);
            writer.WriteFloat(msg.SizeMultiplier);
        }

        public static OrbBomberSequenceStartMessage ReadOrbBomberSequenceStartMessage(
            NetworkReader reader
        )
        {
            return new OrbBomberSequenceStartMessage
            {
                OrbNetId = reader.ReadUInt(),
                Duration = reader.ReadFloat(),
                FlashCount = reader.ReadInt(),
                SizeMultiplier = reader.ReadFloat(),
            };
        }
    }

    public struct OrbBomberSequenceResetMessage : NetworkMessage
    {
        public uint OrbNetId;
    }

    public static class OrbBomberSequenceResetMessageSerialization
    {
        public static void WriteOrbBomberSequenceResetMessage(
            NetworkWriter writer,
            OrbBomberSequenceResetMessage msg
        )
        {
            writer.WriteUInt(msg.OrbNetId);
        }

        public static OrbBomberSequenceResetMessage ReadOrbBomberSequenceResetMessage(
            NetworkReader reader
        )
        {
            return new OrbBomberSequenceResetMessage { OrbNetId = reader.ReadUInt() };
        }
    }

    public struct OrbBomberSwingHitMessage : NetworkMessage
    {
        public uint OrbNetId;
    }

    public static class OrbBomberSwingHitMessageSerialization
    {
        public static void WriteOrbBomberSwingHitMessage(
            NetworkWriter writer,
            OrbBomberSwingHitMessage msg
        )
        {
            writer.WriteUInt(msg.OrbNetId);
        }

        public static OrbBomberSwingHitMessage ReadOrbBomberSwingHitMessage(NetworkReader reader)
        {
            return new OrbBomberSwingHitMessage { OrbNetId = reader.ReadUInt() };
        }
    }
}
