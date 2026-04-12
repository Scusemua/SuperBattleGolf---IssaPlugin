using Mirror;

namespace IssaPlugin.Items
{
    // ── Client → Server ───────────────────────────────────────────────────────
    public struct WindStormActivateMessage : NetworkMessage { }

    // ── Server → All clients: storm begins ───────────────────────────────────
    public struct WindStormBeginMessage : NetworkMessage
    {
        /// NetId of the player whose ball is immune to wind.
        public uint ActivatorNetId;
        public float Duration;
        public int StormSpeed;
    }

    // ── Server → All clients: storm ends ─────────────────────────────────────
    public struct WindStormEndMessage : NetworkMessage { }

    // ── Serialization ─────────────────────────────────────────────────────────
    public static class WindStormMessageSerialization
    {
        public static void WriteActivate(NetworkWriter w, WindStormActivateMessage m) { }

        public static WindStormActivateMessage ReadActivate(NetworkReader r) =>
            new WindStormActivateMessage();

        public static void WriteBegin(NetworkWriter w, WindStormBeginMessage m)
        {
            w.WriteUInt(m.ActivatorNetId);
            w.WriteFloat(m.Duration);
            w.WriteInt(m.StormSpeed);
        }

        public static WindStormBeginMessage ReadBegin(NetworkReader r) =>
            new WindStormBeginMessage
            {
                ActivatorNetId = r.ReadUInt(),
                Duration = r.ReadFloat(),
                StormSpeed = r.ReadInt(),
            };

        public static void WriteEnd(NetworkWriter w, WindStormEndMessage m) { }

        public static WindStormEndMessage ReadEnd(NetworkReader r) => new WindStormEndMessage();
    }
}
