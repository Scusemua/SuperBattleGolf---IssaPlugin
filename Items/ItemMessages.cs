using Mirror;

namespace IssaPlugin.Items
{
    // ── Client → Server ──────────────────────────────────────────────────

    /// Sent by a non-host client when a hotkey is pressed to give themselves an item.
    /// The server checks AllowHotkeyItemGiving before granting the item.
    /// The host bypasses this message entirely and adds items directly.
    public struct GiveItemRequestMessage : NetworkMessage
    {
        public ItemType ItemType;
        public int Uses;
    }

    public static class GiveItemRequestMessageSerialization
    {
        public static void WriteGiveItemRequestMessage(
            NetworkWriter writer,
            GiveItemRequestMessage msg
        )
        {
            writer.WriteInt((int)msg.ItemType);
            writer.WriteInt(msg.Uses);
        }

        public static GiveItemRequestMessage ReadGiveItemRequestMessage(NetworkReader reader)
        {
            return new GiveItemRequestMessage
            {
                ItemType = (ItemType)reader.ReadInt(),
                Uses = reader.ReadInt(),
            };
        }
    }
}
