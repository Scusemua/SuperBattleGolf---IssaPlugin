using Mirror;
using UnityEngine;

namespace IssaPlugin.Items
{
    // ── Client → Server ──────────────────────────────────────────────────────

    public struct DonutStartMessage : NetworkMessage { }

    public struct DonutEndMessage : NetworkMessage { }

    public struct DonutMoveMessage : NetworkMessage
    {
        /// Normalised world-space horizontal direction the Donut should move toward.
        /// Vector3.zero means "stop".
        public Vector3 WorldMoveDir;
    }

    public struct DonutFireLaserMessage : NetworkMessage { }

    public struct DonutShotDownMessage : NetworkMessage
    {
        public Vector3 CrashDir;
        public float CrashSpeed;
        public Vector3 ImpactDir;
        public Vector3 TorqueImpulse;

        public override string ToString()
        {
            return $"DonutShotDownMessage[CrashDir={CrashDir},CrashSpeed={CrashSpeed},ImpactDir={ImpactDir},TorqueImpulse={TorqueImpulse}]";
        }
    }

    // ── Server → Client ──────────────────────────────────────────────────────

    public struct DonutBeginClientMessage : NetworkMessage
    {
        public uint DonutNetId;
    }

    public struct DonutEndClientMessage : NetworkMessage { }

    public struct DonutBusyMessage : NetworkMessage { }

    public struct DonutLaserShootStartMessage : NetworkMessage
    {
        public Vector3 StartPosition;
    }

    // ── Serialization ─────────────────────────────────────────────────────────

    public static class DonutMessageSerialization
    {
        public static void WriteDonutStartMessage(NetworkWriter w, DonutStartMessage m) { }

        public static DonutStartMessage ReadDonutStartMessage(NetworkReader r) => new DonutStartMessage();

        public static void WriteDonutEndMessage(NetworkWriter w, DonutEndMessage m) { }

        public static DonutEndMessage ReadDonutEndMessage(NetworkReader r) => new DonutEndMessage();

        public static void WriteDonutMoveMessage(NetworkWriter w, DonutMoveMessage m)
        {
            w.WriteVector3(m.WorldMoveDir);
        }

        public static DonutMoveMessage ReadDonutMoveMessage(NetworkReader r)
        {
            return new DonutMoveMessage { WorldMoveDir = r.ReadVector3() };
        }

        public static void WriteDonutFireLaserMessage(NetworkWriter w, DonutFireLaserMessage m) { }

        public static DonutFireLaserMessage ReadDonutFireLaserMessage(NetworkReader r) =>
            new DonutFireLaserMessage();

        public static void WriteDonutBeginClientMessage(NetworkWriter w, DonutBeginClientMessage m)
        {
            w.WriteUInt(m.DonutNetId);
        }

        public static DonutBeginClientMessage ReadDonutBeginClientMessage(NetworkReader r)
        {
            return new DonutBeginClientMessage { DonutNetId = r.ReadUInt() };
        }

        public static void WriteDonutEndClientMessage(NetworkWriter w, DonutEndClientMessage m) { }

        public static DonutEndClientMessage ReadDonutEndClientMessage(NetworkReader r) =>
            new DonutEndClientMessage { };

        public static void WriteDonutShotDownMessage(NetworkWriter writer, DonutShotDownMessage msg)
        {
            writer.WriteVector3(msg.CrashDir);
            writer.WriteFloat(msg.CrashSpeed);
            writer.WriteVector3(msg.ImpactDir);
            writer.WriteVector3(msg.TorqueImpulse);
        }

        public static DonutShotDownMessage ReadDonutShotDownMessage(NetworkReader reader)
        {
            return new DonutShotDownMessage
            {
                CrashDir = reader.ReadVector3(),
                CrashSpeed = reader.ReadFloat(),
                ImpactDir = reader.ReadVector3(),
                TorqueImpulse = reader.ReadVector3(),
            };
        }

        public static void WriteDonutBusyMessage(NetworkWriter writer, DonutBusyMessage msg) { }

        public static DonutBusyMessage ReadDonutBusyMessage(NetworkReader reader) =>
            new DonutBusyMessage();

        public static void WriteDonutLaserShootStartMessage(
            NetworkWriter writer,
            DonutLaserShootStartMessage msg
        )
        {
            writer.WriteVector3(msg.StartPosition);
        }

        public static DonutLaserShootStartMessage ReadDonutLaserShootStartMessage(
            NetworkReader reader
        ) => new DonutLaserShootStartMessage { StartPosition = reader.ReadVector3() };
    }
}
