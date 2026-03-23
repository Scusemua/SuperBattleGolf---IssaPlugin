using Mirror;

namespace IssaPlugin.Items
{
    /// <summary>
    /// Sent server → owning client only (via connectionToClient.Send) whenever an
    /// item that the local player deployed — currently Harrier or Bear — successfully
    /// hits another player.  The overlay renders the message as a brief on-screen toast.
    /// </summary>
    public struct HitNotificationMessage : NetworkMessage
    {
        public string Message;
    }

    public static class HitNotificationMessageSerialization
    {
        public static void WriteHitNotificationMessage(
            NetworkWriter writer,
            HitNotificationMessage msg
        ) => writer.WriteString(msg.Message);

        public static HitNotificationMessage ReadHitNotificationMessage(
            NetworkReader reader
        ) => new HitNotificationMessage { Message = reader.ReadString() };
    }
}
