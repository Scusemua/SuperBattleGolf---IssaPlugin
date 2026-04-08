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
        /// <summary>
        /// The client's local EquippedItemIndex when the item was used.
        /// Passed so the server can validate and consume the correct inventory slot
        /// without relying on NetworkedEquippedItemIndex (which is not synced
        /// client→server).
        /// </summary>
        public int EquippedSlotIndex;
    }

    public static class RocketTetherLockOnMessageSerialization
    {
        public static void WriteRocketTetherLockOnMessage(
            NetworkWriter writer,
            RocketTetherLockOnMessage msg
        )
        {
            writer.WriteUInt(msg.TargetNetId);
            writer.WriteInt(msg.EquippedSlotIndex);
        }

        public static RocketTetherLockOnMessage ReadRocketTetherLockOnMessage(
            NetworkReader reader
        ) =>
            new RocketTetherLockOnMessage
            {
                TargetNetId = reader.ReadUInt(),
                EquippedSlotIndex = reader.ReadInt(),
            };
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
    public struct RocketTetherConnectedMessage : NetworkMessage
    {
        /// The player who fired the Rocket Tether.
        public uint WielderNetId;

        /// The player who is being dragged by the rocket.
        public uint TargetNetId;

        /// World-space position where the rocket spawns (just above the target's head).
        public Vector3 RocketStartPos;

        /// Upward speed of the rocket (m/s).
        public float RocketSpeed;

        /// Total flight time before the rocket explodes (seconds).
        public float Duration;
    }

    public static class RocketTetherConnectedMessageSerialization
    {
        public static void WriteRocketTetherConnectedMessage(
            NetworkWriter writer,
            RocketTetherConnectedMessage msg
        )
        {
            writer.WriteUInt(msg.WielderNetId);
            writer.WriteUInt(msg.TargetNetId);
            writer.WriteVector3(msg.RocketStartPos);
            writer.WriteFloat(msg.RocketSpeed);
            writer.WriteFloat(msg.Duration);
        }

        public static RocketTetherConnectedMessage ReadRocketTetherConnectedMessage(
            NetworkReader reader
        ) =>
            new RocketTetherConnectedMessage
            {
                WielderNetId = reader.ReadUInt(),
                TargetNetId = reader.ReadUInt(),
                RocketStartPos = reader.ReadVector3(),
                RocketSpeed = reader.ReadFloat(),
                Duration = reader.ReadFloat(),
            };
    }

    // ─────────────────────────────────────────────────────────────────────────

    /// Broadcast to every client when the rocket explodes.
    /// All clients destroy VFX and apply explosion force to the local player
    /// if they are within ExplosionRadius of ExplosionPosition.
    public struct RocketTetherDisconnectedMessage : NetworkMessage
    {
        /// World-space position of the explosion.
        public Vector3 ExplosionPosition;

        /// Peak force (m/s, VelocityChange) applied at the explosion centre.
        public float ExplosionForce;

        /// Radius (units) within which the explosion affects nearby players.
        public float ExplosionRadius;
    }

    public static class RocketTetherDisconnectedMessageSerialization
    {
        public static void WriteRocketTetherDisconnectedMessage(
            NetworkWriter writer,
            RocketTetherDisconnectedMessage msg
        )
        {
            writer.WriteVector3(msg.ExplosionPosition);
            writer.WriteFloat(msg.ExplosionForce);
            writer.WriteFloat(msg.ExplosionRadius);
        }

        public static RocketTetherDisconnectedMessage ReadRocketTetherDisconnectedMessage(
            NetworkReader reader
        ) =>
            new RocketTetherDisconnectedMessage
            {
                ExplosionPosition = reader.ReadVector3(),
                ExplosionForce = reader.ReadFloat(),
                ExplosionRadius = reader.ReadFloat(),
            };
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
