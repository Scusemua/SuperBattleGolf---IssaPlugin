using Mirror;
using UnityEngine;

namespace IssaPlugin.Items
{
    public struct FreezeBeginMessage : NetworkMessage
    {
        public float Duration;

        public override string ToString()
        {
            return $"FreezeBeginMessage[Duration={Duration}]";
        }
    }

    public struct FreezeEndMessage : NetworkMessage { }

    public static class FreezeBeginMessageSerialization
    {
        public static void WriteFreezeBeginMessage(NetworkWriter writer, FreezeBeginMessage msg)
        {
            writer.WriteFloat(msg.Duration);
        }

        public static FreezeBeginMessage ReadFreezeBeginMessage(NetworkReader reader)
        {
            return new FreezeBeginMessage { Duration = reader.ReadFloat() };
        }
    }

    public static class FreezeEndMessageSerialization
    {
        public static void WriteFreezeEndMessage(NetworkWriter writer, FreezeEndMessage msg) { }

        public static FreezeEndMessage ReadFreezeEndMessage(NetworkReader reader)
        {
            return new FreezeEndMessage { };
        }
    }

    public struct FreezeActivateMessage : NetworkMessage { }

    public static class FreezeActivateMessageSerialization
    {
        public static void WriteFreezeActivateMessage(
            NetworkWriter writer,
            FreezeActivateMessage msg
        ) { }

        public static FreezeActivateMessage ReadFreezeActivateMessage(NetworkReader reader)
        {
            return new FreezeActivateMessage();
        }
    }
}
