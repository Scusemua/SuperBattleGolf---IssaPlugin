using Mirror;

namespace IssaPlugin.Items
{
    public struct NightActivateMessage : NetworkMessage { }

    public struct NightBeginMessage : NetworkMessage
    {
        public float Duration;
        public uint ActivatorNetId;
    }

    public struct NightEndMessage : NetworkMessage { }

    public static class NightMessageSerialization
    {
        public static void WriteActivate(NetworkWriter writer, NightActivateMessage msg) { }

        public static NightActivateMessage ReadActivate(NetworkReader reader) =>
            new NightActivateMessage();

        public static void WriteBegin(NetworkWriter writer, NightBeginMessage msg)
        {
            writer.WriteFloat(msg.Duration);
            writer.WriteUInt(msg.ActivatorNetId);
        }

        public static NightBeginMessage ReadBegin(NetworkReader reader) =>
            new NightBeginMessage
            {
                Duration = reader.ReadFloat(),
                ActivatorNetId = reader.ReadUInt(),
            };

        public static void WriteEnd(NetworkWriter writer, NightEndMessage msg) { }

        public static NightEndMessage ReadEnd(NetworkReader reader) => new NightEndMessage();
    }
}
