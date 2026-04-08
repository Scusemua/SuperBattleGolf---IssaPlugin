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

    /// <summary>Server → all clients: show the backpack prefab on a specific player.
    /// Also carries the host's jetpack config so clients use authoritative values.</summary>
    public struct JetpackEquipBeginMessage : NetworkMessage
    {
        public uint PlayerNetId;
        public float FuelPerUse;
        public float ThrustForce;
    }

    /// <summary>Server → all clients: hide the backpack prefab on a specific player.</summary>
    public struct JetpackEquipEndMessage : NetworkMessage
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

        public static void WriteEquipBegin(NetworkWriter w, JetpackEquipBeginMessage msg)
        {
            w.WriteUInt(msg.PlayerNetId);
            w.WriteFloat(msg.FuelPerUse);
            w.WriteFloat(msg.ThrustForce);
        }

        public static JetpackEquipBeginMessage ReadEquipBegin(NetworkReader r) =>
            new JetpackEquipBeginMessage
            {
                PlayerNetId = r.ReadUInt(),
                FuelPerUse = r.ReadFloat(),
                ThrustForce = r.ReadFloat(),
            };

        public static void WriteEquipEnd(NetworkWriter w, JetpackEquipEndMessage msg) =>
            w.WriteUInt(msg.PlayerNetId);

        public static JetpackEquipEndMessage ReadEquipEnd(NetworkReader r) =>
            new JetpackEquipEndMessage { PlayerNetId = r.ReadUInt() };
    }
}
