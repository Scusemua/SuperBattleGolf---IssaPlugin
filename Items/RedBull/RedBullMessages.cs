using Mirror;

namespace IssaPlugin.Items
{
    /// <summary>Client → Server: local player wants to start the Red Bull trail.</summary>
    public struct RedBullActivateMessage : NetworkMessage { }

    /// <summary>Server → all clients: show the trail on a specific player.</summary>
    public struct RedBullTrailBeginMessage : NetworkMessage
    {
        public uint PlayerNetId;
        public float Duration;
    }

    /// <summary>Server → all clients: remove the trail from a specific player.</summary>
    public struct RedBullTrailEndMessage : NetworkMessage
    {
        public uint PlayerNetId;
    }

    public static class RedBullMessageSerialization
    {
        public static void WriteActivate(NetworkWriter w, RedBullActivateMessage msg) { }

        public static RedBullActivateMessage ReadActivate(NetworkReader r) =>
            new RedBullActivateMessage();

        public static void WriteTrailBegin(NetworkWriter w, RedBullTrailBeginMessage msg)
        {
            w.WriteUInt(msg.PlayerNetId);
            w.WriteFloat(msg.Duration);
        }

        public static RedBullTrailBeginMessage ReadTrailBegin(NetworkReader r) =>
            new RedBullTrailBeginMessage { PlayerNetId = r.ReadUInt(), Duration = r.ReadFloat() };

        public static void WriteTrailEnd(NetworkWriter w, RedBullTrailEndMessage msg) =>
            w.WriteUInt(msg.PlayerNetId);

        public static RedBullTrailEndMessage ReadTrailEnd(NetworkReader r) =>
            new RedBullTrailEndMessage { PlayerNetId = r.ReadUInt() };
    }
}
