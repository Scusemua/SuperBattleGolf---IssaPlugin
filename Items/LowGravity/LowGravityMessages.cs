using Mirror;
using UnityEngine;

namespace IssaPlugin.Items
{
    public struct LowGravityBeginMessage : NetworkMessage
    {
        public float Duration;

        public override string ToString()
        {
            return $"LowGravityBeginMessage[Duration={Duration}]";
        }
    }

    public struct LowGravityEndMessage : NetworkMessage { }

    public static class LowGravityBeginMessageSerialization
    {
        public static void WriteLowGravityBeginMessage(
            NetworkWriter writer,
            LowGravityBeginMessage msg
        )
        {
            writer.WriteFloat(msg.Duration);
        }

        public static LowGravityBeginMessage ReadLowGravityBeginMessage(NetworkReader reader)
        {
            return new LowGravityBeginMessage { Duration = reader.ReadFloat() };
        }
    }

    public static class LowGravityEndMessageSerialization
    {
        public static void WriteLowGravityEndMessage(
            NetworkWriter writer,
            LowGravityEndMessage msg
        ) { }

        public static LowGravityEndMessage ReadLowGravityEndMessage(NetworkReader reader)
        {
            return new LowGravityEndMessage { };
        }
    }

    public struct LowGravityActivateMessage : NetworkMessage { }

    public static class LowGravityActivateMessageSerialization
    {
        public static void WriteLowGravityActivateMessage(
            NetworkWriter writer,
            LowGravityActivateMessage msg
        ) { }

        public static LowGravityActivateMessage ReadLowGravityActivateMessage(NetworkReader reader)
        {
            return new LowGravityActivateMessage();
        }
    }
}
