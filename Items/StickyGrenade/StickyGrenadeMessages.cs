using Mirror;
using UnityEngine;

namespace IssaPlugin.Items
{
    // ── Client → Server ──────────────────────────────────────────────────

    /// Sent when the local player presses use with StickyGrenade equipped.
    /// ThrowOrigin is the world-space spawn point (near the player's hand),
    /// ThrowVelocity is the full initial velocity vector.
    public struct StickyGrenadeThrowMessage : NetworkMessage
    {
        public Vector3 ThrowOrigin;
        public Vector3 ThrowVelocity;
    }

    public static class StickyGrenadeThrowMessageSerialization
    {
        public static void WriteStickyGrenadeThrowMessage(
            NetworkWriter writer,
            StickyGrenadeThrowMessage msg
        )
        {
            writer.WriteVector3(msg.ThrowOrigin);
            writer.WriteVector3(msg.ThrowVelocity);
        }

        public static StickyGrenadeThrowMessage ReadStickyGrenadeThrowMessage(NetworkReader reader)
        {
            return new StickyGrenadeThrowMessage
            {
                ThrowOrigin = reader.ReadVector3(),
                ThrowVelocity = reader.ReadVector3(),
            };
        }
    }

    // ── Server → All Clients ─────────────────────────────────────────────

    /// Broadcast when the grenade sticks to something.
    ///
    /// TargetNetId   — netId of the stuck target's NetworkIdentity (0 if static geometry).
    /// LocalOffset   — position of the grenade in the target's local space (for moving
    ///                 targets) or world space (for static geometry when TargetNetId == 0).
    /// FuseRemaining — seconds until detonation, so late-joining clients can calculate
    ///                 the correct blink frequency immediately.
    public struct StickyGrenadeStuckMessage : NetworkMessage
    {
        public uint GrenadeNetId;
        public uint TargetNetId; // 0 = static/terrain
        public Vector3 LocalOffset;
        public float FuseRemaining;
    }

    public static class StickyGrenadeStuckMessageSerialization
    {
        public static void WriteStickyGrenadeStuckMessage(
            NetworkWriter writer,
            StickyGrenadeStuckMessage msg
        )
        {
            writer.WriteUInt(msg.GrenadeNetId);
            writer.WriteUInt(msg.TargetNetId);
            writer.WriteVector3(msg.LocalOffset);
            writer.WriteFloat(msg.FuseRemaining);
        }

        public static StickyGrenadeStuckMessage ReadStickyGrenadeStuckMessage(NetworkReader reader)
        {
            return new StickyGrenadeStuckMessage
            {
                GrenadeNetId = reader.ReadUInt(),
                TargetNetId = reader.ReadUInt(),
                LocalOffset = reader.ReadVector3(),
                FuseRemaining = reader.ReadFloat(),
            };
        }
    }

    /// Broadcast just before the grenade explodes so all clients can
    /// trigger the explosion VFX locally even if Mirror's Destroy message
    /// arrives before the VFX spawns.
    public struct StickyGrenadeDetonatedMessage : NetworkMessage
    {
        public Vector3 WorldPosition;
    }

    public static class StickyGrenadeDetonatedMessageSerialization
    {
        public static void WriteStickyGrenadeDetonatedMessage(
            NetworkWriter writer,
            StickyGrenadeDetonatedMessage msg
        )
        {
            writer.WriteVector3(msg.WorldPosition);
        }

        public static StickyGrenadeDetonatedMessage ReadStickyGrenadeDetonatedMessage(
            NetworkReader reader
        )
        {
            return new StickyGrenadeDetonatedMessage { WorldPosition = reader.ReadVector3() };
        }
    }
}
