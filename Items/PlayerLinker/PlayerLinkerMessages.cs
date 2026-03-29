using Mirror;
using UnityEngine;

namespace IssaPlugin.Items
{
    // ── Client → Server ──────────────────────────────────────────────────────

    /// Sent when the local player uses the Player Linker with a valid target in sight.
    /// Server validates, acquires the global session lock, and broadcasts
    /// PlayerLinkerConnectedMessage if successful.
    public struct PlayerLinkerLockOnMessage : NetworkMessage
    {
        public uint TargetNetId;
    }

    public static class PlayerLinkerLockOnMessageSerialization
    {
        public static void WritePlayerLinkerLockOnMessage(
            NetworkWriter writer,
            PlayerLinkerLockOnMessage msg
        ) => writer.WriteUInt(msg.TargetNetId);

        public static PlayerLinkerLockOnMessage ReadPlayerLinkerLockOnMessage(
            NetworkReader reader
        ) => new PlayerLinkerLockOnMessage { TargetNetId = reader.ReadUInt() };
    }

    // ─────────────────────────────────────────────────────────────────────────

    /// Sent every PlayerLinkerInputSendInterval (~20 Hz) by BOTH tethered players
    /// while the session is active. Server relays each player's position to the other
    /// so each client's spring coroutine pulls toward the correct point.
    public struct PlayerLinkerTickMessage : NetworkMessage
    {
        /// World-space position of the sending player's character.
        public Vector3 Position;
    }

    public static class PlayerLinkerTickMessageSerialization
    {
        public static void WritePlayerLinkerTickMessage(
            NetworkWriter writer,
            PlayerLinkerTickMessage msg
        ) => writer.WriteVector3(msg.Position);

        public static PlayerLinkerTickMessage ReadPlayerLinkerTickMessage(
            NetworkReader reader
        ) => new PlayerLinkerTickMessage { Position = reader.ReadVector3() };
    }

    // ── Server → All Clients ─────────────────────────────────────────────────

    /// Broadcast to every client when the tether successfully connects.
    /// Both tethered players start their spring coroutines; all clients create
    /// a local LineRenderer for the tether visual.
    public struct PlayerLinkerConnectedMessage : NetworkMessage
    {
        public uint WielderNetId;
        public uint TargetNetId;

        /// Maximum session duration in seconds (used as coroutine timeout guard).
        public float Duration;
    }

    public static class PlayerLinkerConnectedMessageSerialization
    {
        public static void WritePlayerLinkerConnectedMessage(
            NetworkWriter writer,
            PlayerLinkerConnectedMessage msg
        )
        {
            writer.WriteUInt(msg.WielderNetId);
            writer.WriteUInt(msg.TargetNetId);
            writer.WriteFloat(msg.Duration);
        }

        public static PlayerLinkerConnectedMessage ReadPlayerLinkerConnectedMessage(
            NetworkReader reader
        ) =>
            new PlayerLinkerConnectedMessage
            {
                WielderNetId = reader.ReadUInt(),
                TargetNetId = reader.ReadUInt(),
                Duration = reader.ReadFloat(),
            };
    }

    // ─────────────────────────────────────────────────────────────────────────

    /// Forwarded by the server to one tethered client (via targeted Send).
    /// Contains the OTHER player's current world-space position so the
    /// receiving client's spring coroutine can compute the correct pull direction.
    public struct PlayerLinkerRelayMessage : NetworkMessage
    {
        public Vector3 OtherPosition;
    }

    public static class PlayerLinkerRelayMessageSerialization
    {
        public static void WritePlayerLinkerRelayMessage(
            NetworkWriter writer,
            PlayerLinkerRelayMessage msg
        ) => writer.WriteVector3(msg.OtherPosition);

        public static PlayerLinkerRelayMessage ReadPlayerLinkerRelayMessage(
            NetworkReader reader
        ) => new PlayerLinkerRelayMessage { OtherPosition = reader.ReadVector3() };
    }

    // ─────────────────────────────────────────────────────────────────────────

    /// Broadcast to every client when the tether ends (timeout).
    /// All clients stop coroutines and destroy the tether line.
    public struct PlayerLinkerDisconnectedMessage : NetworkMessage { }

    public static class PlayerLinkerDisconnectedMessageSerialization
    {
        public static void WritePlayerLinkerDisconnectedMessage(
            NetworkWriter writer,
            PlayerLinkerDisconnectedMessage msg
        ) { }

        public static PlayerLinkerDisconnectedMessage ReadPlayerLinkerDisconnectedMessage(
            NetworkReader reader
        ) => new PlayerLinkerDisconnectedMessage();
    }

    // ── Server → Wielder Only ────────────────────────────────────────────────

    /// Sent only to the wielder's connection when GlobalSessionLock is held by
    /// another player.  The item is NOT consumed.
    public struct PlayerLinkerBusyMessage : NetworkMessage { }

    public static class PlayerLinkerBusyMessageSerialization
    {
        public static void WritePlayerLinkerBusyMessage(
            NetworkWriter writer,
            PlayerLinkerBusyMessage msg
        ) { }

        public static PlayerLinkerBusyMessage ReadPlayerLinkerBusyMessage(
            NetworkReader reader
        ) => new PlayerLinkerBusyMessage();
    }
}
