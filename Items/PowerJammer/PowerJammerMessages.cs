using Mirror;

namespace IssaPlugin.Items
{
    public struct PowerJammerBeginMessage : NetworkMessage
    {
        public float Duration;
        public uint ActivatorNetId;
        public bool AffectsActivator;
    }

    public struct PowerJammerEndMessage : NetworkMessage { }

    public struct PowerJammerActivateMessage : NetworkMessage { }

    public static class PowerJammerBeginMessageSerialization
    {
        public static void WritePowerJammerBeginMessage(
            NetworkWriter writer,
            PowerJammerBeginMessage msg
        )
        {
            writer.WriteFloat(msg.Duration);
            writer.WriteUInt(msg.ActivatorNetId);
            writer.WriteBool(msg.AffectsActivator);
        }

        public static PowerJammerBeginMessage ReadPowerJammerBeginMessage(
            NetworkReader reader
        )
        {
            return new PowerJammerBeginMessage
            {
                Duration = reader.ReadFloat(),
                ActivatorNetId = reader.ReadUInt(),
                AffectsActivator = reader.ReadBool(),
            };
        }
    }

    public static class PowerJammerEndMessageSerialization
    {
        public static void WritePowerJammerEndMessage(
            NetworkWriter writer,
            PowerJammerEndMessage msg
        ) { }

        public static PowerJammerEndMessage ReadPowerJammerEndMessage(NetworkReader reader) =>
            new PowerJammerEndMessage();
    }

    public static class PowerJammerActivateMessageSerialization
    {
        public static void WritePowerJammerActivateMessage(
            NetworkWriter writer,
            PowerJammerActivateMessage msg
        ) { }

        public static PowerJammerActivateMessage ReadPowerJammerActivateMessage(
            NetworkReader reader
        ) => new PowerJammerActivateMessage();
    }
}
