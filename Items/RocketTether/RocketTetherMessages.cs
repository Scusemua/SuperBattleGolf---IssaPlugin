using Mirror;
using UnityEngine;

namespace IssaPlugin.Items
{
    // ── Client → Server ──────────────────────────────────────────────────────

    /// Sent when the local player uses the Rocket Tether with a valid target in sight.
    /// Server validates, acquires the global session lock, and broadcasts
    /// RocketTetherConnectedMessage if successful.
    public struct RocketTetherLockOnMessage : NetworkMessage
    {
        public uint TargetNetId;
    }

    public static class RocketTetherLockOnMessageSerialization
    {
        public static void WriteRocketTetherLockOnMessage(
            NetworkWriter writer,
            RocketTetherLockOnMessage msg
        ) => writer.WriteUInt(msg.TargetNetId);

        public static RocketTetherLockOnMessage ReadRocketTetherLockOnMessage(
            NetworkReader reader
        ) => new RocketTetherLockOnMessage { TargetNetId = reader.ReadUInt() };
    }

    // ── Server → Wielder Only ────────────────────────────────────────────────

    /// Sent only to the wielder's connection when GlobalSessionLock is held by
    /// another player.  The item is NOT consumed.
    public struct RocketTetherBusyMessage : NetworkMessage { }

    public static class RocketTetherBusyMessageSerialization
    {
        public static void WriteRocketTetherBusyMessage(
            NetworkWriter writer,
            RocketTetherBusyMessage msg
        ) { }

        public static RocketTetherBusyMessage ReadRocketTetherBusyMessage(NetworkReader reader) =>
            new RocketTetherBusyMessage();
    }
}
