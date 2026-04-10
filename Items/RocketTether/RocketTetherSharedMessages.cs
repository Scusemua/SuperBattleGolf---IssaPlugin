using Mirror;
using UnityEngine;

namespace IssaPlugin.Items
{
    // ── Server → All Clients ─────────────────────────────────────────────────
    // Shared by both the Rocket Tether item and the Rocket Tether Grenade.
    // The original per-item Connected/Disconnected messages are replaced by
    // these so that RocketTetherClientLogic can serve both items from a single
    // shared session dictionary.

    /// <summary>
    /// Broadcast to every client when a rocket attaches to a single victim.
    /// Sent once per victim, so the grenade naturally emits one message per
    /// player caught in its AoE radius.
    ///
    /// Spring parameters are embedded here so each item can tune them
    /// independently without requiring separate message types.
    /// </summary>
    public struct RocketVictimConnectedMessage : NetworkMessage
    {
        /// The player who fired / threw the item.
        public uint WielderNetId;

        /// The player being dragged by the rocket.
        public uint VictimNetId;

        /// World-space position where the rocket spawns (just above the victim's head).
        public Vector3 RocketStartPos;

        /// Upward speed of the rocket (m/s).
        public float RocketSpeed;

        /// Total flight time before the rocket explodes (seconds).
        public float Duration;

        /// Spring stiffness coefficient.
        public float SpringForce;

        /// Maximum velocity change (m/s) applied per physics tick.
        public float MaxPullSpeed;

        /// Distance below which no pull force is applied (rope slack, units).
        public float NaturalLength;
    }

    public static class RocketVictimConnectedMessageSerialization
    {
        public static void WriteRocketVictimConnectedMessage(
            NetworkWriter writer,
            RocketVictimConnectedMessage msg
        )
        {
            writer.WriteUInt(msg.WielderNetId);
            writer.WriteUInt(msg.VictimNetId);
            writer.WriteVector3(msg.RocketStartPos);
            writer.WriteFloat(msg.RocketSpeed);
            writer.WriteFloat(msg.Duration);
            writer.WriteFloat(msg.SpringForce);
            writer.WriteFloat(msg.MaxPullSpeed);
            writer.WriteFloat(msg.NaturalLength);
        }

        public static RocketVictimConnectedMessage ReadRocketVictimConnectedMessage(
            NetworkReader reader
        ) =>
            new RocketVictimConnectedMessage
            {
                WielderNetId   = reader.ReadUInt(),
                VictimNetId    = reader.ReadUInt(),
                RocketStartPos = reader.ReadVector3(),
                RocketSpeed    = reader.ReadFloat(),
                Duration       = reader.ReadFloat(),
                SpringForce    = reader.ReadFloat(),
                MaxPullSpeed   = reader.ReadFloat(),
                NaturalLength  = reader.ReadFloat(),
            };
    }

    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Broadcast to every client when a single victim's rocket explodes.
    /// VictimNetId is required here (unlike the old RocketTetherDisconnectedMessage)
    /// because multiple sessions may be active simultaneously when the grenade is used.
    /// </summary>
    public struct RocketVictimDisconnectedMessage : NetworkMessage
    {
        /// The player whose rocket just exploded.
        public uint VictimNetId;

        /// World-space position of the explosion.
        public Vector3 ExplosionPosition;

        /// Peak force (m/s, VelocityChange) applied at the explosion centre.
        public float ExplosionForce;

        /// Radius (units) within which the explosion affects nearby players.
        public float ExplosionRadius;
    }

    public static class RocketVictimDisconnectedMessageSerialization
    {
        public static void WriteRocketVictimDisconnectedMessage(
            NetworkWriter writer,
            RocketVictimDisconnectedMessage msg
        )
        {
            writer.WriteUInt(msg.VictimNetId);
            writer.WriteVector3(msg.ExplosionPosition);
            writer.WriteFloat(msg.ExplosionForce);
            writer.WriteFloat(msg.ExplosionRadius);
        }

        public static RocketVictimDisconnectedMessage ReadRocketVictimDisconnectedMessage(
            NetworkReader reader
        ) =>
            new RocketVictimDisconnectedMessage
            {
                VictimNetId       = reader.ReadUInt(),
                ExplosionPosition = reader.ReadVector3(),
                ExplosionForce    = reader.ReadFloat(),
                ExplosionRadius   = reader.ReadFloat(),
            };
    }
}
