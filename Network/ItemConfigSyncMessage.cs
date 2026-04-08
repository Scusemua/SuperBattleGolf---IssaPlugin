using Mirror;

namespace IssaPlugin.Network
{
    /// <summary>
    /// Server → all clients: full dump of the host's BepInEx config file so clients
    /// use the host's authoritative values instead of their own local defaults.
    /// Sent by ItemConfigSyncer every 5 seconds while the server is active.
    /// </summary>
    public struct ItemConfigSyncMessage : NetworkMessage
    {
        /// <summary>"Section::Key" for each entry.</summary>
        public string[] Keys;
        /// <summary>Serialized value string (BepInEx format) for each entry.</summary>
        public string[] Values;
    }

    public static class ItemConfigSyncSerialization
    {
        public static void Write(NetworkWriter w, ItemConfigSyncMessage msg)
        {
            int count = msg.Keys?.Length ?? 0;
            w.WriteInt(count);
            for (int i = 0; i < count; i++)
            {
                w.WriteString(msg.Keys[i]);
                w.WriteString(msg.Values[i] ?? string.Empty);
            }
        }

        public static ItemConfigSyncMessage Read(NetworkReader r)
        {
            int count = r.ReadInt();
            var keys = new string[count];
            var values = new string[count];
            for (int i = 0; i < count; i++)
            {
                keys[i] = r.ReadString();
                values[i] = r.ReadString();
            }
            return new ItemConfigSyncMessage { Keys = keys, Values = values };
        }
    }
}
