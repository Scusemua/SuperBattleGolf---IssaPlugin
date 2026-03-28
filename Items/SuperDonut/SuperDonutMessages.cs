using Mirror;

namespace IssaPlugin.Items
{
    // ── Client → Server ──────────────────────────────────────────────────────

    public struct SuperDonutStartMessage : NetworkMessage { }

    // ── Server → All Clients ─────────────────────────────────────────────────

    /// Broadcast when the server fires the barrage so all clients can react.
    public struct SuperDonutFiredMessage : NetworkMessage
    {
        /// Number of lasers fired (one per targeted player).
        public int TargetCount;
    }

    // ── Serialization ─────────────────────────────────────────────────────────

    public static class SuperDonutMessageSerialization
    {
        public static void WriteSuperDonutStartMessage(
            NetworkWriter w,
            SuperDonutStartMessage m
        ) { }

        public static SuperDonutStartMessage ReadSuperDonutStartMessage(NetworkReader r) =>
            new SuperDonutStartMessage();

        public static void WriteSuperDonutFiredMessage(NetworkWriter w, SuperDonutFiredMessage m)
        {
            w.WriteInt(m.TargetCount);
        }

        public static SuperDonutFiredMessage ReadSuperDonutFiredMessage(NetworkReader r) =>
            new SuperDonutFiredMessage { TargetCount = r.ReadInt() };
    }
}
