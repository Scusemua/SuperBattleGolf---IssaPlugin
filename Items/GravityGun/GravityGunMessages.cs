using Mirror;
using UnityEngine;

namespace IssaPlugin.Items
{
    // ── Client → Server ──────────────────────────────────────────────────────

    /// Sent when the local player uses the Gravity Gun with a valid target in sight.
    /// Server validates, acquires the global session lock, and broadcasts
    /// GravityGunConnectedMessage if successful.
    public struct GravityGunLockOnMessage : NetworkMessage
    {
        public uint TargetNetId;
    }

    public static class GravityGunLockOnMessageSerialization
    {
        public static void WriteGravityGunLockOnMessage(
            NetworkWriter writer,
            GravityGunLockOnMessage msg
        ) => writer.WriteUInt(msg.TargetNetId);

        public static GravityGunLockOnMessage ReadGravityGunLockOnMessage(NetworkReader reader) =>
            new GravityGunLockOnMessage { TargetNetId = reader.ReadUInt() };
    }

    // ─────────────────────────────────────────────────────────────────────────

    /// Sent every GravityGunInputSendInterval (~20 Hz) by the wielder while
    /// the session is active.  Server forwards this to the target client as
    /// GravityGunTetherTickMessage so the target's spring coroutine knows where
    /// to pull toward.
    public struct GravityGunAimTickMessage : NetworkMessage
    {
        /// World-space position of the wielder's character (NOT camera position).
        public Vector3 WielderPos;

        /// World-space camera forward direction of the wielder.
        public Vector3 AimDir;
    }

    public static class GravityGunAimTickMessageSerialization
    {
        public static void WriteGravityGunAimTickMessage(
            NetworkWriter writer,
            GravityGunAimTickMessage msg
        )
        {
            writer.WriteVector3(msg.WielderPos);
            writer.WriteVector3(msg.AimDir);
        }

        public static GravityGunAimTickMessage ReadGravityGunAimTickMessage(NetworkReader reader) =>
            new GravityGunAimTickMessage
            {
                WielderPos = reader.ReadVector3(),
                AimDir = reader.ReadVector3(),
            };
    }

    // ─────────────────────────────────────────────────────────────────────────

    /// Sent by the wielder when they press the release key.
    /// Server calls ServerEndSession() which broadcasts GravityGunDisconnectedMessage.
    public struct GravityGunReleaseMessage : NetworkMessage { }

    public static class GravityGunReleaseMessageSerialization
    {
        public static void WriteGravityGunReleaseMessage(
            NetworkWriter writer,
            GravityGunReleaseMessage msg
        ) { }

        public static GravityGunReleaseMessage ReadGravityGunReleaseMessage(NetworkReader reader) =>
            new GravityGunReleaseMessage();
    }

    // ── Server → All Clients ─────────────────────────────────────────────────

    /// Broadcast to every client when the tether successfully connects.
    /// All clients create a local LineRenderer; only the target starts the
    /// spring coroutine.
    public struct GravityGunConnectedMessage : NetworkMessage
    {
        public uint WielderNetId;
        public uint TargetNetId;

        /// Radius (units) at which the target orbits the wielder.
        public float TetherRadius;

        /// Maximum session duration in seconds (used as coroutine timeout guard).
        public float Duration;
    }

    public static class GravityGunConnectedMessageSerialization
    {
        public static void WriteGravityGunConnectedMessage(
            NetworkWriter writer,
            GravityGunConnectedMessage msg
        )
        {
            writer.WriteUInt(msg.WielderNetId);
            writer.WriteUInt(msg.TargetNetId);
            writer.WriteFloat(msg.TetherRadius);
            writer.WriteFloat(msg.Duration);
        }

        public static GravityGunConnectedMessage ReadGravityGunConnectedMessage(
            NetworkReader reader
        ) =>
            new GravityGunConnectedMessage
            {
                WielderNetId = reader.ReadUInt(),
                TargetNetId = reader.ReadUInt(),
                TetherRadius = reader.ReadFloat(),
                Duration = reader.ReadFloat(),
            };
    }

    // ─────────────────────────────────────────────────────────────────────────

    /// Forwarded by the server to the target client only (via _targetConn.Send).
    /// Updates the target client's spring coroutine with the wielder's current
    /// position and aim direction so the desired orbit point tracks correctly.
    public struct GravityGunTetherTickMessage : NetworkMessage
    {
        public Vector3 WielderPos;
        public Vector3 AimDir;
    }

    public static class GravityGunTetherTickMessageSerialization
    {
        public static void WriteGravityGunTetherTickMessage(
            NetworkWriter writer,
            GravityGunTetherTickMessage msg
        )
        {
            writer.WriteVector3(msg.WielderPos);
            writer.WriteVector3(msg.AimDir);
        }

        public static GravityGunTetherTickMessage ReadGravityGunTetherTickMessage(
            NetworkReader reader
        ) =>
            new GravityGunTetherTickMessage
            {
                WielderPos = reader.ReadVector3(),
                AimDir = reader.ReadVector3(),
            };
    }

    // ─────────────────────────────────────────────────────────────────────────

    /// Broadcast to every client when the tether ends (manual release or timeout).
    /// All clients stop coroutines and destroy VFX.
    public struct GravityGunDisconnectedMessage : NetworkMessage
    {
        public uint WielderNetId;
    }

    public static class GravityGunDisconnectedMessageSerialization
    {
        public static void WriteGravityGunDisconnectedMessage(
            NetworkWriter writer,
            GravityGunDisconnectedMessage msg
        ) => writer.WriteUInt(msg.WielderNetId);

        public static GravityGunDisconnectedMessage ReadGravityGunDisconnectedMessage(
            NetworkReader reader
        ) => new GravityGunDisconnectedMessage { WielderNetId = reader.ReadUInt() };
    }

    // ── Server → Wielder Only ────────────────────────────────────────────────

    /// Sent only to the wielder's connection when GlobalSessionLock is held by
    /// another player.  The item is NOT consumed.
    public struct GravityGunBusyMessage : NetworkMessage { }

    public static class GravityGunBusyMessageSerialization
    {
        public static void WriteGravityGunBusyMessage(
            NetworkWriter writer,
            GravityGunBusyMessage msg
        ) { }

        public static GravityGunBusyMessage ReadGravityGunBusyMessage(NetworkReader reader) =>
            new GravityGunBusyMessage();
    }
}
