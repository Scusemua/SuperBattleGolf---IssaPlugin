using Mirror;

namespace IssaPlugin.Items
{
    public struct DroppedItemPickupMessage : NetworkMessage
    {
        public uint DroppedItemNetId;
    }

    public static class DroppedItemPickupMessageSerialization
    {
        public static void WriteDroppedItemPickupMessage(
            NetworkWriter writer,
            DroppedItemPickupMessage msg
        )
        {
            writer.WriteUInt(msg.DroppedItemNetId);
        }

        public static DroppedItemPickupMessage ReadDroppedItemPickupMessage(NetworkReader reader)
        {
            return new DroppedItemPickupMessage { DroppedItemNetId = reader.ReadUInt() };
        }
    }
}
