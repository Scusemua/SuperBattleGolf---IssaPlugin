using Mirror;
using UnityEngine;

namespace IssaPlugin.Items
{
    // ── Client → Server ──────────────────────────────────────────────────────

    public struct CannonShootMessage : NetworkMessage
    {
        /// <summary>Normalized world-space direction to launch the cart along.</summary>
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
}
