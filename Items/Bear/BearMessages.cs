using Mirror;
using UnityEngine;

namespace IssaPlugin.Items
{
    // ── Client → Server ──────────────────────────────────────────────────────

    /// <summary>
    /// Sent when the local player activates the Bear item.
    /// The server validates ownership and spawns the bears.
    /// Renamed from BearSummonMessage to resolve a Mirror message ID collision with JetpackThrustStartMessage (both hashed to id=48286).
    /// </summary>
    public struct BearActivateMessage : NetworkMessage { }

    /// <summary>
    /// Sent by the local client during the swing hit window when a bear is
    /// detected inside the swing hitbox area.  The server validates the hit
    /// and applies damage + knockback immediately — without waiting for
    /// <c>OnFinishedSwinging</c> — eliminating the visible reaction delay.
    /// </summary>
    public struct BearSwingHitMessage : NetworkMessage
    {
        /// Network identity of the bear that entered the swing hitbox.
        public uint BearNetId;
    }

    public static class BearSwingHitMessageSerialization
    {
        public static void WriteBearSwingHitMessage(
            NetworkWriter writer,
            BearSwingHitMessage msg
        ) => writer.WriteUInt(msg.BearNetId);

        public static BearSwingHitMessage ReadBearSwingHitMessage(NetworkReader reader) =>
            new BearSwingHitMessage { BearNetId = reader.ReadUInt() };
    }

    public static class BearActivateMessageSerialization
    {
        public static void WriteBearActivateMessage(NetworkWriter writer, BearActivateMessage msg) { }

        public static BearActivateMessage ReadBearActivateMessage(NetworkReader reader) =>
            new BearActivateMessage();
    }

    // ── Server → All Clients ─────────────────────────────────────────────────

    /// <summary>
    /// Broadcast whenever a bear transitions to a new AI state.
    /// Clients use this to drive their local Animator.
    /// Sent only on state *change* — not every frame.
    /// </summary>
    public struct BearStateMessage : NetworkMessage
    {
        /// Network identity of the bear whose state changed.
        public uint BearNetId;

        /// The new state the bear has entered.
        public BearAIState State;

        /// Current world position of the bear's locked target (used by clients
        /// for predictive facing so the bear looks at something meaningful even
        /// between NetworkTransform updates). Zero if no target.
        public Vector3 TargetPosition;
    }

    public static class BearStateMessageSerialization
    {
        public static void WriteBearStateMessage(NetworkWriter writer, BearStateMessage msg)
        {
            writer.WriteUInt(msg.BearNetId);
            writer.WriteByte((byte)msg.State);
            writer.WriteVector3(msg.TargetPosition);
        }

        public static BearStateMessage ReadBearStateMessage(NetworkReader reader)
        {
            return new BearStateMessage
            {
                BearNetId = reader.ReadUInt(),
                State = (BearAIState)reader.ReadByte(),
                TargetPosition = reader.ReadVector3(),
            };
        }
    }

    /// <summary>
    /// Broadcast when a bear lands an attack on a player.
    /// Clients use this to play an impact sound / camera shake locally.
    /// The actual damage / knockback is applied server-side.
    /// </summary>
    public struct BearAttackImpactMessage : NetworkMessage
    {
        public uint BearNetId;
        public Vector3 ImpactPosition;
    }

    public static class BearAttackImpactMessageSerialization
    {
        public static void WriteBearAttackImpactMessage(
            NetworkWriter writer,
            BearAttackImpactMessage msg
        )
        {
            writer.WriteUInt(msg.BearNetId);
            writer.WriteVector3(msg.ImpactPosition);
        }

        public static BearAttackImpactMessage ReadBearAttackImpactMessage(NetworkReader reader)
        {
            return new BearAttackImpactMessage
            {
                BearNetId = reader.ReadUInt(),
                ImpactPosition = reader.ReadVector3(),
            };
        }
    }

    /// <summary>
    /// Broadcast to all clients whenever a bear is hit by a gun or melee weapon.
    /// Each client spawns a blood splatter at <see cref="HitPoint"/> aimed from
    /// <see cref="AttackerOrigin"/> (barrel end for guns, player position for melee).
    /// </summary>
    public struct BearHitVfxMessage : NetworkMessage
    {
        public Vector3 HitPoint;
        public Vector3 AttackerOrigin;
    }

    public static class BearHitVfxMessageSerialization
    {
        public static void WriteBearHitVfxMessage(NetworkWriter writer, BearHitVfxMessage msg)
        {
            writer.WriteVector3(msg.HitPoint);
            writer.WriteVector3(msg.AttackerOrigin);
        }

        public static BearHitVfxMessage ReadBearHitVfxMessage(NetworkReader reader) =>
            new BearHitVfxMessage
            {
                HitPoint = reader.ReadVector3(),
                AttackerOrigin = reader.ReadVector3(),
            };
    }

    /// <summary>
    /// Broadcast to all clients when all bears from a session have died or
    /// the session timeout expires, so clients can clean up any local VFX.
    /// </summary>
    public struct BearHuntEndedMessage : NetworkMessage { }

    public static class BearHuntEndedMessageSerialization
    {
        public static void WriteBearHuntEndedMessage(
            NetworkWriter writer,
            BearHuntEndedMessage msg
        ) { }

        public static BearHuntEndedMessage ReadBearHuntEndedMessage(NetworkReader reader) =>
            new BearHuntEndedMessage();
    }

    /// <summary>
    /// Broadcast to all clients whenever a bear's HP changes.
    /// Clients use this to update the world-space health bar above the bear.
    /// </summary>
    public struct BearHPUpdateMessage : NetworkMessage
    {
        public uint BearNetId;
        public float CurrentHP;
        public float MaxHP;
    }

    public static class BearHPUpdateMessageSerialization
    {
        public static void WriteBearHPUpdateMessage(NetworkWriter writer, BearHPUpdateMessage msg)
        {
            writer.WriteUInt(msg.BearNetId);
            writer.WriteFloat(msg.CurrentHP);
            writer.WriteFloat(msg.MaxHP);
        }

        public static BearHPUpdateMessage ReadBearHPUpdateMessage(NetworkReader reader) =>
            new BearHPUpdateMessage
            {
                BearNetId = reader.ReadUInt(),
                CurrentHP = reader.ReadFloat(),
                MaxHP = reader.ReadFloat(),
            };
    }

    // ── Server → Owning Client only (TargetRpc replacements) ────────────────
    // These are sent via connectionToClient.Send() so only the summoning player
    // receives them — not every player in the session.

    /// <summary>
    /// Sent to the summoning client when bears are first spawned.
    /// Tells the client to show the bear HUD with the correct pip count and timer.
    /// </summary>
    public struct BearOverlayBeginMessage : NetworkMessage
    {
        public int BearCount;
        public float SessionDuration;
    }

    public static class BearOverlayBeginMessageSerialization
    {
        public static void WriteBearOverlayBeginMessage(
            NetworkWriter writer,
            BearOverlayBeginMessage msg
        )
        {
            writer.WriteInt(msg.BearCount);
            writer.WriteFloat(msg.SessionDuration);
        }

        public static BearOverlayBeginMessage ReadBearOverlayBeginMessage(NetworkReader reader)
        {
            return new BearOverlayBeginMessage
            {
                BearCount = reader.ReadInt(),
                SessionDuration = reader.ReadFloat(),
            };
        }
    }

    /// <summary>
    /// Sent to the summoning client each time one of their bears is killed.
    /// The client decrements the HUD pip count on receipt.
    /// </summary>
    public struct BearKilledClientMessage : NetworkMessage { }

    public static class BearKilledClientMessageSerialization
    {
        public static void WriteBearKilledClientMessage(
            NetworkWriter writer,
            BearKilledClientMessage msg
        ) { }

        public static BearKilledClientMessage ReadBearKilledClientMessage(NetworkReader reader) =>
            new BearKilledClientMessage();
    }

    /// <summary>
    /// Sent to the summoning client when the session ends (timeout or all bears dead).
    /// Tells the client to hide the bear HUD.
    /// </summary>
    public struct BearOverlayEndMessage : NetworkMessage { }

    public static class BearOverlayEndMessageSerialization
    {
        public static void WriteBearOverlayEndMessage(
            NetworkWriter writer,
            BearOverlayEndMessage msg
        ) { }

        public static BearOverlayEndMessage ReadBearOverlayEndMessage(NetworkReader reader) =>
            new BearOverlayEndMessage();
    }
}
