using Mirror;
using UnityEngine;

namespace IssaPlugin.Items
{
    // ── Client → Server ──────────────────────────────────────────────────────

    /// Sent when the local player uses the UFO Abduction item with a valid lock-on target.
    public struct UfoAbductionLockOnMessage : NetworkMessage
    {
        public uint TargetNetId;
    }

    public static class UfoAbductionLockOnMessageSerialization
    {
        public static void WriteUfoAbductionLockOnMessage(
            NetworkWriter writer,
            UfoAbductionLockOnMessage msg
        ) => writer.WriteUInt(msg.TargetNetId);

        public static UfoAbductionLockOnMessage ReadUfoAbductionLockOnMessage(
            NetworkReader reader
        ) => new UfoAbductionLockOnMessage { TargetNetId = reader.ReadUInt() };
    }

    // ── Server → Wielder only ────────────────────────────────────────────────

    /// Sent only to the wielder's connection when another abduction session is already active.
    public struct UfoAbductionBusyMessage : NetworkMessage { }

    public static class UfoAbductionBusyMessageSerialization
    {
        public static void WriteUfoAbductionBusyMessage(
            NetworkWriter writer,
            UfoAbductionBusyMessage msg
        ) { }

        public static UfoAbductionBusyMessage ReadUfoAbductionBusyMessage(NetworkReader reader) =>
            new UfoAbductionBusyMessage();
    }

    // ── Server → All Clients ─────────────────────────────────────────────────

    /// Broadcast when a UFO abduction session begins.
    /// All clients use this to animate the UFO and apply physics to the victim.
    public struct UfoAbductionBeginMessage : NetworkMessage
    {
        public uint WielderNetId;
        public uint VictimNetId;

        /// UFO appears at this position and flies toward HoverPos during approach.
        public Vector3 UfoSpawnPos;

        /// UFO hovers here during the abduction phase, pulling the victim up.
        public Vector3 HoverPos;

        /// UFO (and victim) fly to this position during ascent, then explode.
        public Vector3 ExplosionPos;

        public float ApproachDuration;
        public float AbductionDuration;
        public float AscentDuration;

        public float SpringForce;
        public float MaxPullSpeed;
        public float NaturalLength;

        public float ExplosionForce;
        public float ExplosionRadius;

        /// Vertical distance from victim to hover point; used by clients to
        /// recompute HoverPos against the victim's live position at approach end.
        public float HoverHeight;

        /// Additional vertical distance from hover point to explosion point.
        public float AscentHeight;
    }

    public static class UfoAbductionBeginMessageSerialization
    {
        public static void WriteUfoAbductionBeginMessage(
            NetworkWriter writer,
            UfoAbductionBeginMessage msg
        )
        {
            writer.WriteUInt(msg.WielderNetId);
            writer.WriteUInt(msg.VictimNetId);
            writer.WriteVector3(msg.UfoSpawnPos);
            writer.WriteVector3(msg.HoverPos);
            writer.WriteVector3(msg.ExplosionPos);
            writer.WriteFloat(msg.ApproachDuration);
            writer.WriteFloat(msg.AbductionDuration);
            writer.WriteFloat(msg.AscentDuration);
            writer.WriteFloat(msg.SpringForce);
            writer.WriteFloat(msg.MaxPullSpeed);
            writer.WriteFloat(msg.NaturalLength);
            writer.WriteFloat(msg.ExplosionForce);
            writer.WriteFloat(msg.ExplosionRadius);
            writer.WriteFloat(msg.HoverHeight);
            writer.WriteFloat(msg.AscentHeight);
        }

        public static UfoAbductionBeginMessage ReadUfoAbductionBeginMessage(NetworkReader reader) =>
            new UfoAbductionBeginMessage
            {
                WielderNetId = reader.ReadUInt(),
                VictimNetId = reader.ReadUInt(),
                UfoSpawnPos = reader.ReadVector3(),
                HoverPos = reader.ReadVector3(),
                ExplosionPos = reader.ReadVector3(),
                ApproachDuration = reader.ReadFloat(),
                AbductionDuration = reader.ReadFloat(),
                AscentDuration = reader.ReadFloat(),
                SpringForce = reader.ReadFloat(),
                MaxPullSpeed = reader.ReadFloat(),
                NaturalLength = reader.ReadFloat(),
                ExplosionForce = reader.ReadFloat(),
                ExplosionRadius = reader.ReadFloat(),
                HoverHeight = reader.ReadFloat(),
                AscentHeight = reader.ReadFloat(),
            };
    }

    /// Broadcast when the UFO explodes and the victim is released.
    public struct UfoAbductionEndMessage : NetworkMessage
    {
        public uint VictimNetId;
        public Vector3 ExplosionPos;
        public float ExplosionForce;
        public float ExplosionRadius;
    }

    public static class UfoAbductionEndMessageSerialization
    {
        public static void WriteUfoAbductionEndMessage(
            NetworkWriter writer,
            UfoAbductionEndMessage msg
        )
        {
            writer.WriteUInt(msg.VictimNetId);
            writer.WriteVector3(msg.ExplosionPos);
            writer.WriteFloat(msg.ExplosionForce);
            writer.WriteFloat(msg.ExplosionRadius);
        }

        public static UfoAbductionEndMessage ReadUfoAbductionEndMessage(NetworkReader reader) =>
            new UfoAbductionEndMessage
            {
                VictimNetId = reader.ReadUInt(),
                ExplosionPos = reader.ReadVector3(),
                ExplosionForce = reader.ReadFloat(),
                ExplosionRadius = reader.ReadFloat(),
            };
    }
}
