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

    /// Sent when the wielder confirms a drop-off destination in the targeting UI.
    public struct UfoAbductionDropoffSelectedMessage : NetworkMessage
    {
        public Vector3 Destination;
    }

    public static class UfoAbductionDropoffSelectedMessageSerialization
    {
        public static void WriteUfoAbductionDropoffSelectedMessage(
            NetworkWriter writer,
            UfoAbductionDropoffSelectedMessage msg
        ) => writer.WriteVector3(msg.Destination);

        public static UfoAbductionDropoffSelectedMessage ReadUfoAbductionDropoffSelectedMessage(
            NetworkReader reader
        ) => new UfoAbductionDropoffSelectedMessage { Destination = reader.ReadVector3() };
    }

    /// Sent when the wielder explicitly cancels the drop-off targeting UI (Space / RMB).
    /// NOT sent during hole transitions or disconnects — the server handles those itself.
    public struct UfoAbductionDropoffCancelledMessage : NetworkMessage { }

    public static class UfoAbductionDropoffCancelledMessageSerialization
    {
        public static void WriteUfoAbductionDropoffCancelledMessage(
            NetworkWriter writer,
            UfoAbductionDropoffCancelledMessage msg
        ) { }

        public static UfoAbductionDropoffCancelledMessage ReadUfoAbductionDropoffCancelledMessage(
            NetworkReader reader
        ) => new UfoAbductionDropoffCancelledMessage();
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

    /// Sent only to the wielder's connection after the target is validated.
    /// Tells the wielder's client to open the drop-zone targeting UI.
    public struct UfoAbductionTargetAcquiredMessage : NetworkMessage
    {
        /// Victim's position at lock-on time; used to position the targeting camera.
        public Vector3 VictimPos;
    }

    public static class UfoAbductionTargetAcquiredMessageSerialization
    {
        public static void WriteUfoAbductionTargetAcquiredMessage(
            NetworkWriter writer,
            UfoAbductionTargetAcquiredMessage msg
        ) => writer.WriteVector3(msg.VictimPos);

        public static UfoAbductionTargetAcquiredMessage ReadUfoAbductionTargetAcquiredMessage(
            NetworkReader reader
        ) => new UfoAbductionTargetAcquiredMessage { VictimPos = reader.ReadVector3() };
    }

    // ── Server → Victim only ─────────────────────────────────────────────────

    /// Sent only to the victim's connection when they are locked on as a target.
    /// Shows a "YOU ARE BEING TARGETED" warning banner.
    public struct UfoAbductionBeingTargetedMessage : NetworkMessage { }

    public static class UfoAbductionBeingTargetedMessageSerialization
    {
        public static void WriteUfoAbductionBeingTargetedMessage(
            NetworkWriter writer,
            UfoAbductionBeingTargetedMessage msg
        ) { }

        public static UfoAbductionBeingTargetedMessage ReadUfoAbductionBeingTargetedMessage(
            NetworkReader reader
        ) => new UfoAbductionBeingTargetedMessage();
    }

    // ── Server → All Clients ─────────────────────────────────────────────────

    /// Broadcast when the pre-begin session is cancelled for any reason
    /// (wielder cancelled, selection timed out, hole transition).
    /// All clients use this to clear any pending targeting/victim-warning UI state.
    public struct UfoAbductionSessionAbortedMessage : NetworkMessage { }

    public static class UfoAbductionSessionAbortedMessageSerialization
    {
        public static void WriteUfoAbductionSessionAbortedMessage(
            NetworkWriter writer,
            UfoAbductionSessionAbortedMessage msg
        ) { }

        public static UfoAbductionSessionAbortedMessage ReadUfoAbductionSessionAbortedMessage(
            NetworkReader reader
        ) => new UfoAbductionSessionAbortedMessage();
    }

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

        /// UFO flies to this position during transit (= destination + DropHeight).
        /// Victim is sucked in here and released to fall.
        public Vector3 DropoffPos;

        public float ApproachDuration;
        public float AbductionDuration;
        public float TransitDuration;

        public float MaxPullSpeed;
        public float NaturalLength;

        public float ExplosionForce;
        public float ExplosionRadius;

        /// Vertical distance from victim to hover point; used by clients to
        /// recompute HoverPos against the victim's live position at approach end.
        public float HoverHeight;
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
            writer.WriteVector3(msg.DropoffPos);
            writer.WriteFloat(msg.ApproachDuration);
            writer.WriteFloat(msg.AbductionDuration);
            writer.WriteFloat(msg.TransitDuration);
            writer.WriteFloat(msg.MaxPullSpeed);
            writer.WriteFloat(msg.NaturalLength);
            writer.WriteFloat(msg.ExplosionForce);
            writer.WriteFloat(msg.ExplosionRadius);
            writer.WriteFloat(msg.HoverHeight);
        }

        public static UfoAbductionBeginMessage ReadUfoAbductionBeginMessage(NetworkReader reader) =>
            new UfoAbductionBeginMessage
            {
                WielderNetId = reader.ReadUInt(),
                VictimNetId = reader.ReadUInt(),
                UfoSpawnPos = reader.ReadVector3(),
                HoverPos = reader.ReadVector3(),
                DropoffPos = reader.ReadVector3(),
                ApproachDuration = reader.ReadFloat(),
                AbductionDuration = reader.ReadFloat(),
                TransitDuration = reader.ReadFloat(),
                MaxPullSpeed = reader.ReadFloat(),
                NaturalLength = reader.ReadFloat(),
                ExplosionForce = reader.ReadFloat(),
                ExplosionRadius = reader.ReadFloat(),
                HoverHeight = reader.ReadFloat(),
            };
    }

    /// Broadcast when the UFO releases the victim at the destination and the explosion fires.
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
