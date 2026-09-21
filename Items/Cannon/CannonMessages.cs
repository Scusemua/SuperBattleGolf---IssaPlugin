using Mirror;
using UnityEngine;

namespace IssaPlugin.Items
{
    // ── Client → Server ──────────────────────────────────────────────────────

    public struct CannonShootMessage : NetworkMessage
    {
        /// <summary>Normalized world-space direction to launch the ball along.</summary>
        public Vector3 Direction;

        /// <summary>
        /// The client's EquippedItemIndex when the shot was fired.
        ///
        /// The server validates against this slot in the authoritative `slots` SyncList
        /// rather than against the live equipped item. On a host the client's own
        /// decrement is applied immediately while this message only reaches the server
        /// when Mirror drains the local connection queue, so a liveness check races the
        /// consumption and rejects shots non-deterministically.
        /// </summary>
        public int EquippedSlotIndex;
    }

    /// <summary>
    /// Debug request: spawn a bowling ball aimed at the sender from TestFireDistance away.
    /// Does not consume a Cannon use and does not require the item equipped.
    /// </summary>
    public struct CannonTestFireAtSelfMessage : NetworkMessage { }

    // ── Server → Client ──────────────────────────────────────────────────────

    /// <summary>
    /// Tells the victim's client to run TryKnockOut locally.
    ///
    /// Same pattern as Nuke / Flamethrower: PlayerMovement.TryKnockOut ends in a
    /// Command that only works when issued by an active client that owns the
    /// movement. Server-side collision therefore notifies the victim's machine
    /// rather than calling TryKnockOut on the server copy.
    /// </summary>
    public struct BowlingBallKnockoutMessage : NetworkMessage
    {
        public uint ThrowerNetId;
        public Vector3 LocalHitPoint;
        public float Distance;
        public Vector3 IncomingVelocity;
        public ItemUseId ItemUseId;
    }

    // ── Serialization ────────────────────────────────────────────────────────

    public static class CannonShootMessageSerialization
    {
        public static void WriteCannonShootMessage(NetworkWriter w, CannonShootMessage msg)
        {
            w.WriteVector3(msg.Direction);
            w.WriteInt(msg.EquippedSlotIndex);
        }

        public static CannonShootMessage ReadCannonShootMessage(NetworkReader r) =>
            new() { Direction = r.ReadVector3(), EquippedSlotIndex = r.ReadInt() };
    }

    public static class CannonTestFireAtSelfMessageSerialization
    {
        public static void WriteCannonTestFireAtSelfMessage(
            NetworkWriter w,
            CannonTestFireAtSelfMessage msg
        ) { }

        public static CannonTestFireAtSelfMessage ReadCannonTestFireAtSelfMessage(NetworkReader r) =>
            new();
    }

    public static class BowlingBallKnockoutMessageSerialization
    {
        public static void WriteBowlingBallKnockoutMessage(
            NetworkWriter w,
            BowlingBallKnockoutMessage msg
        )
        {
            w.WriteUInt(msg.ThrowerNetId);
            w.WriteVector3(msg.LocalHitPoint);
            w.WriteFloat(msg.Distance);
            w.WriteVector3(msg.IncomingVelocity);
            w.Write(msg.ItemUseId);
        }

        public static BowlingBallKnockoutMessage ReadBowlingBallKnockoutMessage(NetworkReader r) =>
            new()
            {
                ThrowerNetId = r.ReadUInt(),
                LocalHitPoint = r.ReadVector3(),
                Distance = r.ReadFloat(),
                IncomingVelocity = r.ReadVector3(),
                ItemUseId = r.Read<ItemUseId>(),
            };
    }
}
