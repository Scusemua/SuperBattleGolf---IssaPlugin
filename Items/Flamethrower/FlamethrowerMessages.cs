using Mirror;
using UnityEngine;

namespace IssaPlugin.Items
{
    // ── Client → Server ──────────────────────────────────────────────────────

    /// <summary>Local player started holding the fire button with the flamethrower equipped.</summary>
    public struct FlamethrowerFireStartMessage : NetworkMessage { }

    /// <summary>Local player released the fire button (or fuel ran out).</summary>
    public struct FlamethrowerFireStopMessage : NetworkMessage { }

    /// <summary>
    /// Local player's flame collider touched a specific player.
    /// Server validates and decides whether to start/refresh a burn session.
    /// </summary>
    public struct FlamethrowerBurnRequestMessage : NetworkMessage
    {
        public uint VictimNetId;
    }

    // ── Server → All Clients ─────────────────────────────────────────────────

    /// <summary>Server → all: show flame particles on a specific player (they are now firing).</summary>
    public struct FlamethrowerFireBeginMessage : NetworkMessage
    {
        public uint ShooterNetId;
    }

    /// <summary>Server → all: hide flame particles on a specific player (they stopped firing).</summary>
    public struct FlamethrowerFireEndMessage : NetworkMessage
    {
        public uint ShooterNetId;
    }

    /// <summary>Server → all: a player has been set on fire.</summary>
    public struct FlamethrowerBurnBeginMessage : NetworkMessage
    {
        public uint VictimNetId;
    }

    /// <summary>
    /// Server → all: a player's burn timer expired.
    /// The victim's client applies a knockout impulse; all clients clean up VFX.
    /// </summary>
    public struct FlamethrowerBurnEndMessage : NetworkMessage
    {
        public uint VictimNetId;

        /// <summary>
        /// NetId of the player who triggered the burn. Used on the victim's client
        /// to populate TryKnockOut's responsiblePlayer parameter.
        /// Zero if the shooter is no longer in the session.
        /// </summary>
        public uint ShooterNetId;

        /// <summary>
        /// Horizontal impulse applied to the victim at knockdown. Computed
        /// server-side from the victim's current facing direction so all clients
        /// can apply the same force without querying local state.
        /// </summary>
        public Vector3 KnockImpulse;
    }

    // ── Serialization ────────────────────────────────────────────────────────

    public static class FlamethrowerMessageSerialization
    {
        // ---- FireStart ----
        public static void WriteFireStart(NetworkWriter w, FlamethrowerFireStartMessage msg) { }

        public static FlamethrowerFireStartMessage ReadFireStart(NetworkReader r) => new();

        // ---- FireStop ----
        public static void WriteFireStop(NetworkWriter w, FlamethrowerFireStopMessage msg) { }

        public static FlamethrowerFireStopMessage ReadFireStop(NetworkReader r) => new();

        // ---- BurnRequest ----
        public static void WriteBurnRequest(NetworkWriter w, FlamethrowerBurnRequestMessage msg) =>
            w.WriteUInt(msg.VictimNetId);

        public static FlamethrowerBurnRequestMessage ReadBurnRequest(NetworkReader r) =>
            new() { VictimNetId = r.ReadUInt() };

        // ---- FireBegin ----
        public static void WriteFireBegin(NetworkWriter w, FlamethrowerFireBeginMessage msg) =>
            w.WriteUInt(msg.ShooterNetId);

        public static FlamethrowerFireBeginMessage ReadFireBegin(NetworkReader r) =>
            new() { ShooterNetId = r.ReadUInt() };

        // ---- FireEnd ----
        public static void WriteFireEnd(NetworkWriter w, FlamethrowerFireEndMessage msg) =>
            w.WriteUInt(msg.ShooterNetId);

        public static FlamethrowerFireEndMessage ReadFireEnd(NetworkReader r) =>
            new() { ShooterNetId = r.ReadUInt() };

        // ---- BurnBegin ----
        public static void WriteBurnBegin(NetworkWriter w, FlamethrowerBurnBeginMessage msg) =>
            w.WriteUInt(msg.VictimNetId);

        public static FlamethrowerBurnBeginMessage ReadBurnBegin(NetworkReader r) =>
            new() { VictimNetId = r.ReadUInt() };

        // ---- BurnEnd ----
        public static void WriteBurnEnd(NetworkWriter w, FlamethrowerBurnEndMessage msg)
        {
            w.WriteUInt(msg.VictimNetId);
            w.WriteUInt(msg.ShooterNetId);
            w.WriteVector3(msg.KnockImpulse);
        }

        public static FlamethrowerBurnEndMessage ReadBurnEnd(NetworkReader r) =>
            new()
            {
                VictimNetId = r.ReadUInt(),
                ShooterNetId = r.ReadUInt(),
                KnockImpulse = r.ReadVector3(),
            };
    }
}
