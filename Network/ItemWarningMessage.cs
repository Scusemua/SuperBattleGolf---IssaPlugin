using Mirror;

namespace IssaPlugin.Network
{
    /// <summary>
    /// Broadcast from server to all clients when a player activates a warning-enabled item.
    /// </summary>
    public struct ItemWarningMessage : NetworkMessage
    {
        /// Display name of the player who used the item.
        public string PlayerName;

        /// Human-readable item name shown in the warning banner.
        public string ItemDisplayName;

        /// ItemType cast to int — used to pick per-item PiP camera behaviour.
        public int ItemTypeValue;

        /// netId of the primary visual object to track in the PiP camera.
        /// 0 = no netId-based tracking (use item-type-specific static or no PiP).
        public uint TrackedNetId;

        /// netId of the activating player's NetworkIdentity — used by each client
        /// to suppress the PiP for the player who triggered the warning.
        public uint SenderNetId;
    }

    public static class ItemWarningMessageSerialization
    {
        public static void WriteItemWarningMessage(NetworkWriter writer, ItemWarningMessage msg)
        {
            writer.WriteString(msg.PlayerName);
            writer.WriteString(msg.ItemDisplayName);
            writer.WriteInt(msg.ItemTypeValue);
            writer.WriteUInt(msg.TrackedNetId);
            writer.WriteUInt(msg.SenderNetId);
        }

        public static ItemWarningMessage ReadItemWarningMessage(NetworkReader reader)
        {
            return new ItemWarningMessage
            {
                PlayerName = reader.ReadString(),
                ItemDisplayName = reader.ReadString(),
                ItemTypeValue = reader.ReadInt(),
                TrackedNetId = reader.ReadUInt(),
                SenderNetId = reader.ReadUInt(),
            };
        }
    }
}
