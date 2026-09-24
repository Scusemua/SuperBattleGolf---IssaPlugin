using Mirror;
using UnityEngine;

namespace IssaPlugin.Items
{
    public enum GloveReleaseReason : byte
    {
        Throw = 0,
        Timeout = 1,
        Knockout = 2,
        Interrupt = 3,
        Cleanup = 4,
    }

    // ── Client → Server ──────────────────────────────────────────────────

    public struct GlovePickupRequestMessage : NetworkMessage
    {
        public int EquippedSlotIndex;
    }

    public static class GlovePickupRequestMessageSerialization
    {
        public static void WriteGlovePickupRequestMessage(
            NetworkWriter w,
            GlovePickupRequestMessage msg
        ) => w.WriteInt(msg.EquippedSlotIndex);

        public static GlovePickupRequestMessage ReadGlovePickupRequestMessage(NetworkReader r) =>
            new() { EquippedSlotIndex = r.ReadInt() };
    }

    /// <summary>
    /// Client reports aim direction and a clamped charge fraction.
    /// Server re-clamps charge01 and computes speed — never trusts raw speed.
    /// </summary>
    public struct GloveThrowRequestMessage : NetworkMessage
    {
        public uint SessionId;
        public Vector3 AimDirection;
        public float Charge01;
    }

    public static class GloveThrowRequestMessageSerialization
    {
        public static void WriteGloveThrowRequestMessage(
            NetworkWriter w,
            GloveThrowRequestMessage msg
        )
        {
            w.WriteUInt(msg.SessionId);
            w.WriteVector3(msg.AimDirection);
            w.WriteFloat(msg.Charge01);
        }

        public static GloveThrowRequestMessage ReadGloveThrowRequestMessage(NetworkReader r) =>
            new()
            {
                SessionId = r.ReadUInt(),
                AimDirection = r.ReadVector3(),
                Charge01 = r.ReadFloat(),
            };
    }

    // ── Server → Clients ─────────────────────────────────────────────────

    public struct GloveHoldStartedMessage : NetworkMessage
    {
        public uint HolderNetId;
        public uint SessionId;
        public float Duration;
        public float TimeRemaining;
    }

    public static class GloveHoldStartedMessageSerialization
    {
        public static void WriteGloveHoldStartedMessage(NetworkWriter w, GloveHoldStartedMessage msg)
        {
            w.WriteUInt(msg.HolderNetId);
            w.WriteUInt(msg.SessionId);
            w.WriteFloat(msg.Duration);
            w.WriteFloat(msg.TimeRemaining);
        }

        public static GloveHoldStartedMessage ReadGloveHoldStartedMessage(NetworkReader r) =>
            new()
            {
                HolderNetId = r.ReadUInt(),
                SessionId = r.ReadUInt(),
                Duration = r.ReadFloat(),
                TimeRemaining = r.ReadFloat(),
            };
    }

    public struct GloveReleasedMessage : NetworkMessage
    {
        public uint HolderNetId;
        public uint SessionId;
        public GloveReleaseReason Reason;
        public Vector3 WorldPosition;
        public Vector3 Velocity;
        /// <summary>
        /// Spinach (etc.) speed multiplier baked into <see cref="Velocity"/>.
        /// Clients use this to scale air drag the same way club hits do.
        /// </summary>
        public float PowerMultiplier;
    }

    public static class GloveReleasedMessageSerialization
    {
        public static void WriteGloveReleasedMessage(NetworkWriter w, GloveReleasedMessage msg)
        {
            w.WriteUInt(msg.HolderNetId);
            w.WriteUInt(msg.SessionId);
            w.WriteByte((byte)msg.Reason);
            w.WriteVector3(msg.WorldPosition);
            w.WriteVector3(msg.Velocity);
            w.WriteFloat(msg.PowerMultiplier > 0f ? msg.PowerMultiplier : 1f);
        }

        public static GloveReleasedMessage ReadGloveReleasedMessage(NetworkReader r) =>
            new()
            {
                HolderNetId = r.ReadUInt(),
                SessionId = r.ReadUInt(),
                Reason = (GloveReleaseReason)r.ReadByte(),
                WorldPosition = r.ReadVector3(),
                Velocity = r.ReadVector3(),
                PowerMultiplier = Mathf.Max(1f, r.ReadFloat()),
            };
    }

    /// <summary>
    /// Catch-up payload for a late-joining client: every currently active hold.
    /// </summary>
    public struct GloveActiveHoldsMessage : NetworkMessage
    {
        public GloveHoldStartedMessage[] Holds;
    }

    public static class GloveActiveHoldsMessageSerialization
    {
        public static void WriteGloveActiveHoldsMessage(NetworkWriter w, GloveActiveHoldsMessage msg)
        {
            int count = msg.Holds?.Length ?? 0;
            w.WriteInt(count);
            for (int i = 0; i < count; i++)
                GloveHoldStartedMessageSerialization.WriteGloveHoldStartedMessage(w, msg.Holds[i]);
        }

        public static GloveActiveHoldsMessage ReadGloveActiveHoldsMessage(NetworkReader r)
        {
            int count = r.ReadInt();
            var holds = new GloveHoldStartedMessage[Mathf.Max(0, count)];
            for (int i = 0; i < holds.Length; i++)
                holds[i] = GloveHoldStartedMessageSerialization.ReadGloveHoldStartedMessage(r);
            return new GloveActiveHoldsMessage { Holds = holds };
        }
    }
}
