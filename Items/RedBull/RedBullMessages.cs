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

    /// <summary>Server → all clients: replace the coffee can visual with a Red Bull can.</summary>
    public struct CoffeeDispenserRedBullMessage : NetworkMessage
    {
        public uint NetId;
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

        public static void WriteCoffeeDispenserRedBull(
            NetworkWriter w,
            CoffeeDispenserRedBullMessage msg
        ) => w.WriteUInt(msg.NetId);

        public static CoffeeDispenserRedBullMessage ReadCoffeeDispenserRedBull(
            NetworkReader r
        ) => new() { NetId = r.ReadUInt() };
    }
}
