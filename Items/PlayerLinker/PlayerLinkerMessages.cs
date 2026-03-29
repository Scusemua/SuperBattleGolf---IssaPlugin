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

    // ── Server → All Clients ─────────────────────────────────────────────────

    /// Broadcast to every client when the rocket successfully attaches.
    /// All clients instantiate the local rocket VFX and draw the tether line;
    /// the target client starts the spring-force coroutine.
    ///
    /// Because the rocket travels at a constant velocity upward, clients can
    /// compute its position deterministically as:
    ///   rocketPos = RocketStartPos + Vector3.up * RocketSpeed * elapsed
    /// No per-tick relay messages are needed.
    public struct PlayerLinkerConnectedMessage : NetworkMessage
    {
        /// The player who is being dragged by the rocket.
        public uint TargetNetId;

        /// World-space position where the rocket spawns (just above the target's head).
        public Vector3 RocketStartPos;

        /// Upward speed of the rocket (m/s).
        public float RocketSpeed;

        /// Total flight time before the rocket explodes (seconds).
        public float Duration;
    }

    public static class PlayerLinkerConnectedMessageSerialization
    {
        public static void WritePlayerLinkerConnectedMessage(
            NetworkWriter writer,
            PlayerLinkerConnectedMessage msg
        )
        {
            writer.WriteUInt(msg.TargetNetId);
            writer.WriteVector3(msg.RocketStartPos);
            writer.WriteFloat(msg.RocketSpeed);
            writer.WriteFloat(msg.Duration);
        }

        public static PlayerLinkerConnectedMessage ReadPlayerLinkerConnectedMessage(
            NetworkReader reader
        ) =>
            new PlayerLinkerConnectedMessage
            {
                TargetNetId    = reader.ReadUInt(),
                RocketStartPos = reader.ReadVector3(),
                RocketSpeed    = reader.ReadFloat(),
                Duration       = reader.ReadFloat(),
            };
    }

    // ─────────────────────────────────────────────────────────────────────────

    /// Broadcast to every client when the rocket explodes.
    /// All clients destroy VFX and apply explosion force to the local player
    /// if they are within ExplosionRadius of ExplosionPosition.
    public struct PlayerLinkerDisconnectedMessage : NetworkMessage
    {
        /// World-space position of the explosion.
        public Vector3 ExplosionPosition;

        /// Peak force (m/s, VelocityChange) applied at the explosion centre.
        public float ExplosionForce;

        /// Radius (units) within which the explosion affects nearby players.
        public float ExplosionRadius;
    }

    public static class PlayerLinkerDisconnectedMessageSerialization
    {
        public static void WritePlayerLinkerDisconnectedMessage(
            NetworkWriter writer,
            PlayerLinkerDisconnectedMessage msg
        )
        {
            writer.WriteVector3(msg.ExplosionPosition);
            writer.WriteFloat(msg.ExplosionForce);
            writer.WriteFloat(msg.ExplosionRadius);
        }

        public static PlayerLinkerDisconnectedMessage ReadPlayerLinkerDisconnectedMessage(
            NetworkReader reader
        ) =>
            new PlayerLinkerDisconnectedMessage
            {
                ExplosionPosition = reader.ReadVector3(),
                ExplosionForce    = reader.ReadFloat(),
                ExplosionRadius   = reader.ReadFloat(),
            };
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
