using Mirror;

namespace IssaPlugin.Items
{
    public struct SportsCarActivateMessage : NetworkMessage { }

    public static class SportsCarActivateMessageSerialization
    {
        public static void WriteSportsCarActivateMessage(NetworkWriter writer, SportsCarActivateMessage msg) { }

        public static SportsCarActivateMessage ReadSportsCarActivateMessage(NetworkReader reader)
            => new SportsCarActivateMessage();
    }
}
