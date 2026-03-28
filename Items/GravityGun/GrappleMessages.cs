using Mirror;
using UnityEngine;

namespace IssaPlugin.Items
{
    // ── Client → Server ──────────────────────────────────────────────────────

    /// Sent when the local player uses the Grapple with a valid target in sight.
    /// Server validates, acquires the global session lock, and broadcasts
    /// GrappleConnectedMessage if successful.
    public struct GrappleLockOnMessage : NetworkMessage
    {
        public uint TargetNetId;
    }

    public static class GrappleLockOnMessageSerialization
    {
        public static void WriteGrappleLockOnMessage(
            NetworkWriter writer,
            GrappleLockOnMessage msg
        ) => writer.WriteUInt(msg.TargetNetId);

        public static GrappleLockOnMessage ReadGrappleLockOnMessage(NetworkReader reader) =>
            new GrappleLockOnMessage { TargetNetId = reader.ReadUInt() };
    }

    // ─────────────────────────────────────────────────────────────────────────

    /// Sent every GrappleInputSendInterval (~20 Hz) by the wielder while
    /// the session is active.  Server forwards this to the target client as
    /// GrappleTetherTickMessage so the target's spring coroutine knows where
    /// to pull toward.
    public struct GrappleAimTickMessage : NetworkMessage
    {
        /// World-space position of the wielder's character (NOT camera position).
        public Vector3 WielderPos;

        /// World-space camera forward direction of the wielder.
        public Vector3 AimDir;
    }

    public static class GrappleAimTickMessageSerialization
    {
        public static void WriteGrappleAimTickMessage(
            NetworkWriter writer,
            GrappleAimTickMessage msg
        )
        {
            writer.WriteVector3(msg.WielderPos);
            writer.WriteVector3(msg.AimDir);
        }

        public static GrappleAimTickMessage ReadGrappleAimTickMessage(NetworkReader reader) =>
            new GrappleAimTickMessage
            {
                WielderPos = reader.ReadVector3(),
                AimDir = reader.ReadVector3(),
            };
    }

    // ─────────────────────────────────────────────────────────────────────────

    /// Sent by the wielder when they press the release key.
    /// Server calls ServerEndSession() which broadcasts GrappleDisconnectedMessage.
    public struct GrappleReleaseMessage : NetworkMessage { }

    public static class GrappleReleaseMessageSerialization
    {
        public static void WriteGrappleReleaseMessage(
            NetworkWriter writer,
            GrappleReleaseMessage msg
        ) { }

        public static GrappleReleaseMessage ReadGrappleReleaseMessage(NetworkReader reader) =>
            new GrappleReleaseMessage();
    }

    // ── Server → All Clients ─────────────────────────────────────────────────

    /// Broadcast to every client when the tether successfully connects.
    /// All clients create a local LineRenderer; only the target starts the
    /// spring coroutine.
    public struct GrappleConnectedMessage : NetworkMessage
    {
        public uint WielderNetId;
        public uint TargetNetId;

        /// Radius (units) at which the target orbits the wielder.
        public float TetherRadius;

        /// Maximum session duration in seconds (used as coroutine timeout guard).
        public float Duration;
    }

    public static class GrappleConnectedMessageSerialization
    {
        public static void WriteGrappleConnectedMessage(
            NetworkWriter writer,
            GrappleConnectedMessage msg
        )
        {
            writer.WriteUInt(msg.WielderNetId);
            writer.WriteUInt(msg.TargetNetId);
            writer.WriteFloat(msg.TetherRadius);
            writer.WriteFloat(msg.Duration);
        }

        public static GrappleConnectedMessage ReadGrappleConnectedMessage(NetworkReader reader) =>
            new GrappleConnectedMessage
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
    public struct GrappleTetherTickMessage : NetworkMessage
    {
        public Vector3 WielderPos;
        public Vector3 AimDir;
    }

    public static class GrappleTetherTickMessageSerialization
    {
        public static void WriteGrappleTetherTickMessage(
            NetworkWriter writer,
            GrappleTetherTickMessage msg
        )
        {
            writer.WriteVector3(msg.WielderPos);
            writer.WriteVector3(msg.AimDir);
        }

        public static GrappleTetherTickMessage ReadGrappleTetherTickMessage(NetworkReader reader) =>
            new GrappleTetherTickMessage
            {
                WielderPos = reader.ReadVector3(),
                AimDir = reader.ReadVector3(),
            };
    }

    // ─────────────────────────────────────────────────────────────────────────

    /// Broadcast to every client when the tether ends (manual release or timeout).
    /// All clients stop coroutines and destroy VFX.
    public struct GrappleDisconnectedMessage : NetworkMessage
    {
        public uint WielderNetId;
    }

    public static class GrappleDisconnectedMessageSerialization
    {
        public static void WriteGrappleDisconnectedMessage(
            NetworkWriter writer,
            GrappleDisconnectedMessage msg
        ) => writer.WriteUInt(msg.WielderNetId);

        public static GrappleDisconnectedMessage ReadGrappleDisconnectedMessage(
            NetworkReader reader
        ) => new GrappleDisconnectedMessage { WielderNetId = reader.ReadUInt() };
    }

    // ── Server → Wielder Only ────────────────────────────────────────────────

    /// Sent only to the wielder's connection when GlobalSessionLock is held by
    /// another player.  The item is NOT consumed.
    public struct GrappleBusyMessage : NetworkMessage { }

    public static class GrappleBusyMessageSerialization
    {
        public static void WriteGrappleBusyMessage(NetworkWriter writer, GrappleBusyMessage msg) { }

        public static GrappleBusyMessage ReadGrappleBusyMessage(NetworkReader reader) =>
            new GrappleBusyMessage();
    }
}
