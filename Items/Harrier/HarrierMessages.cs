using Mirror;
using UnityEngine;

namespace IssaPlugin.Items
{
    /// <summary>Sent by the owning client when they activate the Harrier item.</summary>
    public struct HarrierRequestMessage : NetworkMessage { }

    public static class HarrierRequestMessageSerialization
    {
        public static void WriteHarrierRequestMessage(NetworkWriter writer, HarrierRequestMessage msg) {}

        public static HarrierRequestMessage ReadHarrierRequestMessage(NetworkReader reader) =>
            new HarrierRequestMessage {};
    }

    /// <summary>
    /// Broadcast by the server to all clients when a Harrier session begins.
    /// Clients can use this for audio, overlay notifications, etc.
    /// </summary>
    public struct HarrierBeginClientMessage : NetworkMessage
    {
        public Vector3 HoverCenter;
    }

    public static class HarrierBeginClientMessageSerialization
    {
        public static void WriteHarrierBeginClientMessage(NetworkWriter writer, HarrierBeginClientMessage msg)
        {
            writer.WriteVector3(msg.HoverCenter);
        }

        public static HarrierBeginClientMessage ReadHarrierBeginClientMessage(NetworkReader reader) =>
            new HarrierBeginClientMessage
            {
                HoverCenter = reader.ReadVector3(),
            };
    }

    /// <summary>Broadcast by the server to all clients when the Harrier session ends.</summary>
    public struct HarrierEndClientMessage : NetworkMessage { }

    public static class HarrierEndClientMessageSerialization
    {
        public static void WriteHarrierEndClientMessage(NetworkWriter writer, HarrierEndClientMessage msg) {}

        public static HarrierEndClientMessage ReadHarrierEndClientMessage(NetworkReader reader) =>
            new HarrierEndClientMessage {};
    }

    /// <summary>
    /// Sent by the owning client every frame while the Harrier is their active lock-on target.
    /// The server uses this to flag the next rocket for homing toward the Harrier.
    /// </summary>
    public struct HarrierPrepareHomingMessage : NetworkMessage { }

    public static class HarrierPrepareHomingMessageSerialization
    {
        public static void WriteHarrierPrepareHomingMessage(NetworkWriter writer, HarrierPrepareHomingMessage msg) {}

        public static HarrierPrepareHomingMessage ReadHarrierPrepareHomingMessage(NetworkReader reader) =>
            new HarrierPrepareHomingMessage {};
    }

    /// <summary>
    /// Broadcast by the server to all clients when the Harrier is shot down.
    /// Clients can use this for audio / HUD feedback.
    /// </summary>
    public struct HarrierShotDownMessage : NetworkMessage
    {
        public uint HarrierNetId;

        public override string ToString()
        {
            return $"HarrierShotDownMessage[HarrierNetId={HarrierNetId}]";
        }
    }

    public static class HarrierShotDownMessageSerialization
    {
        public static void WriteHarrierShotDownMessage(NetworkWriter writer, HarrierShotDownMessage msg)
        {
            writer.WriteUInt(msg.HarrierNetId);
        }

        public static HarrierShotDownMessage ReadHarrierShotDownMessage(NetworkReader reader) =>
            new HarrierShotDownMessage
            {
                HarrierNetId = reader.ReadUInt(),
            };
    }
}
