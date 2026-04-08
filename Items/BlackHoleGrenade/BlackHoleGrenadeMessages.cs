using Mirror;
using UnityEngine;

namespace IssaPlugin.Items
{
    // ── Client → Server ──────────────────────────────────────────────────

    /// Sent when the local player presses use with BlackHoleGrenade equipped.
    /// ThrowOrigin is the world-space spawn point (near the player's hand),
    /// ThrowVelocity is the full initial velocity vector.
    public struct BlackHoleGrenadeThrowMessage : NetworkMessage
    {
        public Vector3 ThrowOrigin;
        public Vector3 ThrowVelocity;
    }

    public static class BlackHoleGrenadeThrowMessageSerialization
    {
        public static void WriteBlackHoleGrenadeThrowMessage(
            NetworkWriter writer,
            BlackHoleGrenadeThrowMessage msg
        )
        {
            writer.WriteVector3(msg.ThrowOrigin);
            writer.WriteVector3(msg.ThrowVelocity);
        }

        public static BlackHoleGrenadeThrowMessage ReadBlackHoleGrenadeThrowMessage(
            NetworkReader reader
        ) =>
            new BlackHoleGrenadeThrowMessage
            {
                ThrowOrigin = reader.ReadVector3(),
                ThrowVelocity = reader.ReadVector3(),
            };
    }

    // ── Server → All Clients ─────────────────────────────────────────────

    /// Broadcast when the black hole grenade lands and begins suctioning.
    /// Clients use this to start applying suction force to their local player.
    public struct BlackHoleGrenadeLandedMessage : NetworkMessage
    {
        /// netId of the black hole NetworkIdentity — used to pair with the spit message.
        public uint BlackHoleNetId;
        public Vector3 BlackHolePosition;

        /// Who threw the grenade (used for ExcludeThrower config check).
        public PlayerInfo ThrowerInfo;

        /// Seconds the suction phase lasts (client coroutine timeout guard).
        public float SuckDuration;

        /// Radius of the suction field.
        public float SuckRadius;

        /// Suction acceleration (m/s²) applied at the outer edge of the field.
        public float SuckForce;

        /// Suction acceleration (m/s²) applied right at the center.
        public float MaxSuckForce;
    }

    public static class BlackHoleGrenadeLandedMessageSerialization
    {
        public static void WriteBlackHoleGrenadeLandedMessage(
            NetworkWriter writer,
            BlackHoleGrenadeLandedMessage msg
        )
        {
            writer.WriteUInt(msg.BlackHoleNetId);
            writer.WriteVector3(msg.BlackHolePosition);
            writer.WriteNetworkBehaviour(msg.ThrowerInfo);
            writer.WriteFloat(msg.SuckDuration);
            writer.WriteFloat(msg.SuckRadius);
            writer.WriteFloat(msg.SuckForce);
            writer.WriteFloat(msg.MaxSuckForce);
        }

        public static BlackHoleGrenadeLandedMessage ReadBlackHoleGrenadeLandedMessage(
            NetworkReader reader
        ) =>
            new BlackHoleGrenadeLandedMessage
            {
                BlackHoleNetId = reader.ReadUInt(),
                BlackHolePosition = reader.ReadVector3(),
                ThrowerInfo = reader.ReadNetworkBehaviour<PlayerInfo>(),
                SuckDuration = reader.ReadFloat(),
                SuckRadius = reader.ReadFloat(),
                SuckForce = reader.ReadFloat(),
                MaxSuckForce = reader.ReadFloat(),
            };
    }

    /// Broadcast when the suction phase ends and the black hole ejects everything.
    /// Clients use this to stop suction and apply a random outward impulse to their player.
    public struct BlackHoleGrenadeSpitMessage : NetworkMessage
    {
        /// netId of the black hole that is spitting — pairs with BlackHoleGrenadeLandedMessage.
        public uint BlackHoleNetId;
        public Vector3 BlackHolePosition;

        /// Radius within which players receive the spit impulse.
        public float SpitRadius;

        /// Speed (m/s) applied to all ejected objects.
        public float SpitForce;
        public PlayerInfo ThrowerInfo;
        public ItemUseId ItemUseId;
        public float BlackHoleGrenadeSpitVfxScale;
    }

    public static class BlackHoleGrenadeSpitMessageSerialization
    {
        public static void WriteBlackHoleGrenadeSpitMessage(
            NetworkWriter writer,
            BlackHoleGrenadeSpitMessage msg
        )
        {
            writer.WriteUInt(msg.BlackHoleNetId);
            writer.WriteVector3(msg.BlackHolePosition);
            writer.WriteFloat(msg.SpitRadius);
            writer.WriteFloat(msg.SpitForce);
            writer.WriteNetworkBehaviour(msg.ThrowerInfo);
            writer.Write(msg.ItemUseId);
            writer.Write(msg.BlackHoleGrenadeSpitVfxScale);
        }

        public static BlackHoleGrenadeSpitMessage ReadBlackHoleGrenadeSpitMessage(
            NetworkReader reader
        ) =>
            new BlackHoleGrenadeSpitMessage
            {
                BlackHoleNetId = reader.ReadUInt(),
                BlackHolePosition = reader.ReadVector3(),
                SpitRadius = reader.ReadFloat(),
                SpitForce = reader.ReadFloat(),
                ThrowerInfo = reader.ReadNetworkBehaviour<PlayerInfo>(),
                ItemUseId = reader.Read<ItemUseId>(),
                BlackHoleGrenadeSpitVfxScale = reader.ReadFloat(),
            };
    }
}
