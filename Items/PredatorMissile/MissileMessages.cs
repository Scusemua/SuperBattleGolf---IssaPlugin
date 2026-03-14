using Mirror;
using UnityEngine;

namespace IssaPlugin.Items
{
    public struct MissileRequestMessage : NetworkMessage { }

    public static class MissileRequestMessageSerialization
    {
        public static void WriteMissileRequestMessage(
            NetworkWriter writer,
            MissileRequestMessage msg
        ) { }

        public static MissileRequestMessage ReadMissileRequestMessage(NetworkReader reader)
        {
            return new MissileRequestMessage();
        }
    }

    public struct MissileSetVelocityMessage : NetworkMessage
    {
        public Vector3 Velocity;
    }

    public static class MissileSetVelocityMessageSerialization
    {
        public static void WriteMissileSetVelocityMessage(
            NetworkWriter writer,
            MissileSetVelocityMessage msg
        )
        {
            writer.WriteVector3(msg.Velocity);
        }

        public static MissileSetVelocityMessage ReadMissileSetVelocityMessage(NetworkReader reader)
        {
            return new MissileSetVelocityMessage { Velocity = reader.ReadVector3() };
        }
    }

    public struct MissileDetonateMessage : NetworkMessage { }

    public static class MissileDetonateMessageSerialization
    {
        public static void WriteMissileDetonateMessage(
            NetworkWriter writer,
            MissileDetonateMessage msg
        ) { }

        public static MissileDetonateMessage ReadMissileDetonateMessage(NetworkReader reader)
        {
            return new MissileDetonateMessage();
        }
    }

    public struct MissileBeginSteeringMessage : NetworkMessage
    {
        public uint RocketNetId;
    }

    public static class MissileBeginSteeringMessageSerialization
    {
        public static void WriteMissileBeginSteeringMessage(
            NetworkWriter writer,
            MissileBeginSteeringMessage msg
        )
        {
            writer.WriteUInt(msg.RocketNetId);
        }

        public static MissileBeginSteeringMessage ReadMissileBeginSteeringMessage(
            NetworkReader reader
        )
        {
            return new MissileBeginSteeringMessage { RocketNetId = reader.ReadUInt() };
        }
    }

    public struct MissileEndSteeringMessage : NetworkMessage { }

    public static class MissileEndSteeringMessageSerialization
    {
        public static void WriteMissileEndSteeringMessage(
            NetworkWriter writer,
            MissileEndSteeringMessage msg
        ) { }

        public static MissileEndSteeringMessage ReadMissileEndSteeringMessage(NetworkReader reader)
        {
            return new MissileEndSteeringMessage();
        }
    }
}
