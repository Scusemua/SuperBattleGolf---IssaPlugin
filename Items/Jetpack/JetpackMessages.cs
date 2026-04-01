using Mirror;

namespace IssaPlugin.Items
{
    /// <summary>Client → Server: local player started holding the fire button.</summary>
    public struct JetpackThrustStartMessage : NetworkMessage { }

    /// <summary>Client → Server: local player released the fire button (or fuel ran out).</summary>
    public struct JetpackThrustStopMessage : NetworkMessage { }

    /// <summary>Server → all clients: show thrust particles on a specific player.</summary>
    public struct JetpackThrustBeginMessage : NetworkMessage
    {
        public uint PlayerNetId;
    }

    /// <summary>Server → all clients: hide thrust particles on a specific player.</summary>
    public struct JetpackThrustEndMessage : NetworkMessage
    {
        public uint PlayerNetId;
    }

    public static class JetpackMessageSerialization
    {
        public static void WriteThrustStart(NetworkWriter w, JetpackThrustStartMessage msg) { }

        public static JetpackThrustStartMessage ReadThrustStart(NetworkReader r) =>
            new JetpackThrustStartMessage();

        public static void WriteThrustStop(NetworkWriter w, JetpackThrustStopMessage msg) { }

        public static JetpackThrustStopMessage ReadThrustStop(NetworkReader r) =>
            new JetpackThrustStopMessage();

        public static void WriteThrustBegin(NetworkWriter w, JetpackThrustBeginMessage msg) =>
            w.WriteUInt(msg.PlayerNetId);

        public static JetpackThrustBeginMessage ReadThrustBegin(NetworkReader r) =>
            new JetpackThrustBeginMessage { PlayerNetId = r.ReadUInt() };

        public static void WriteThrustEnd(NetworkWriter w, JetpackThrustEndMessage msg) =>
            w.WriteUInt(msg.PlayerNetId);

        public static JetpackThrustEndMessage ReadThrustEnd(NetworkReader r) =>
            new JetpackThrustEndMessage { PlayerNetId = r.ReadUInt() };
    }
}
