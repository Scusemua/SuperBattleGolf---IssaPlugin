using Mirror;
using UnityEngine;

namespace IssaPlugin.Items
{
    // ── Client → Server ──────────────────────────────────────────────────────

    public struct UFOStartMessage : NetworkMessage { }

    public struct UFOEndMessage : NetworkMessage { }

    public struct UFOMoveMessage : NetworkMessage
    {
        /// Normalised world-space horizontal direction the UFO should move toward.
        /// Vector3.zero means "stop".
        public Vector3 WorldMoveDir;
    }

    public struct UFOFireLaserMessage : NetworkMessage { }

    public struct UFOShotDownMessage : NetworkMessage
    {
        public Vector3 CrashDir;
        public float CrashSpeed;
        public Vector3 ImpactDir;
        public Vector3 TorqueImpulse;

        public override string ToString()
        {
            return $"UFOShotDownMessage[CrashDir={CrashDir},CrashSpeed={CrashSpeed},ImpactDir={ImpactDir},TorqueImpulse={TorqueImpulse}]";
        }
    }

    // ── Server → Client ──────────────────────────────────────────────────────

    public struct UFOBeginClientMessage : NetworkMessage
    {
        public uint UFONetId;
    }

    public struct UFOEndClientMessage : NetworkMessage { }

    public struct UFOBusyMessage : NetworkMessage { }

    public static class UFOBusyMessageSerialization
    {
        public static void WriteUFOBusyMessage(NetworkWriter writer, UFOBusyMessage msg) { }

        public static UFOBusyMessage ReadUFOBusyMessage(NetworkReader reader) =>
            new UFOBusyMessage();
    }

    // ── Serialization ─────────────────────────────────────────────────────────

    public static class UFOMessageSerialization
    {
        public static void WriteUFOStartMessage(NetworkWriter w, UFOStartMessage m) { }

        public static UFOStartMessage ReadUFOStartMessage(NetworkReader r) => new UFOStartMessage();

        public static void WriteUFOEndMessage(NetworkWriter w, UFOEndMessage m) { }

        public static UFOEndMessage ReadUFOEndMessage(NetworkReader r) => new UFOEndMessage();

        public static void WriteUFOMoveMessage(NetworkWriter w, UFOMoveMessage m)
        {
            w.WriteVector3(m.WorldMoveDir);
        }

        public static UFOMoveMessage ReadUFOMoveMessage(NetworkReader r)
        {
            return new UFOMoveMessage { WorldMoveDir = r.ReadVector3() };
        }

        public static void WriteUFOFireLaserMessage(NetworkWriter w, UFOFireLaserMessage m) { }

        public static UFOFireLaserMessage ReadUFOFireLaserMessage(NetworkReader r) =>
            new UFOFireLaserMessage();

        public static void WriteUFOBeginClientMessage(NetworkWriter w, UFOBeginClientMessage m)
        {
            w.WriteUInt(m.UFONetId);
        }

        public static UFOBeginClientMessage ReadUFOBeginClientMessage(NetworkReader r)
        {
            return new UFOBeginClientMessage { UFONetId = r.ReadUInt() };
        }

        public static void WriteUFOEndClientMessage(NetworkWriter w, UFOEndClientMessage m) { }

        public static UFOEndClientMessage ReadUFOEndClientMessage(NetworkReader r) =>
            new UFOEndClientMessage { };

        public static void WriteUFOShotDownMessage(NetworkWriter writer, UFOShotDownMessage msg)
        {
            writer.WriteVector3(msg.CrashDir);
            writer.WriteFloat(msg.CrashSpeed);
            writer.WriteVector3(msg.ImpactDir);
            writer.WriteVector3(msg.TorqueImpulse);
        }

        public static UFOShotDownMessage ReadUFOShotDownMessage(NetworkReader reader)
        {
            return new UFOShotDownMessage
            {
                CrashDir = reader.ReadVector3(),
                CrashSpeed = reader.ReadFloat(),
                ImpactDir = reader.ReadVector3(),
                TorqueImpulse = reader.ReadVector3(),
            };
        }
    }
}
