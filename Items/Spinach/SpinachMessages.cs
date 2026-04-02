using Mirror;

namespace IssaPlugin.Items
{
    /// <summary>Client → Server: local player used Spinach.</summary>
    public struct SpinachActivateMessage : NetworkMessage { }

    /// <summary>Server → all clients: show the speed trail on a specific player.</summary>
    public struct SpinachTrailBeginMessage : NetworkMessage
    {
        public uint PlayerNetId;
        public float Duration;
    }

    /// <summary>Server → all clients: remove the trail from a specific player.</summary>
    public struct SpinachTrailEndMessage : NetworkMessage
    {
        public uint PlayerNetId;
    }

    public static class SpinachMessageSerialization
    {
        public static void WriteActivate(NetworkWriter w, SpinachActivateMessage msg) { }

        public static SpinachActivateMessage ReadActivate(NetworkReader r) =>
            new SpinachActivateMessage();

        public static void WriteTrailBegin(NetworkWriter w, SpinachTrailBeginMessage msg)
        {
            w.WriteUInt(msg.PlayerNetId);
            w.WriteFloat(msg.Duration);
        }

        public static SpinachTrailBeginMessage ReadTrailBegin(NetworkReader r) =>
            new SpinachTrailBeginMessage { PlayerNetId = r.ReadUInt(), Duration = r.ReadFloat() };

        public static void WriteTrailEnd(NetworkWriter w, SpinachTrailEndMessage msg) =>
            w.WriteUInt(msg.PlayerNetId);

        public static SpinachTrailEndMessage ReadTrailEnd(NetworkReader r) =>
            new SpinachTrailEndMessage { PlayerNetId = r.ReadUInt() };
    }
}
