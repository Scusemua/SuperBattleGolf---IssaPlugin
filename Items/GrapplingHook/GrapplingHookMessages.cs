using Mirror;
using UnityEngine;

namespace IssaPlugin.Items
{
    public struct GrappleFireMessage : NetworkMessage
    {
        public Vector3 Anchor;
        public int SlotIndex;
        public int Token;
        public uint TargetNetId;
        public Vector3 LocalPoint;
    }

    public struct GrappleAnchorMessage : NetworkMessage
    {
        public uint PlayerNetId;
        public Vector3 Anchor;
        public int Token;
        public int Generation;
        public uint TargetNetId;
        public Vector3 LocalPoint;
    }

    public struct GrappleRejectMessage : NetworkMessage
    {
        public int Token;
    }

    public struct GrappleReleaseMessage : NetworkMessage { }

    public struct GrappleClearMessage : NetworkMessage
    {
        public uint PlayerNetId;
        public int Token;
        public int Generation;
    }

    public static class GrappleMessageSerialization
    {
        public static void WriteFire(NetworkWriter writer, GrappleFireMessage msg)
        {
            writer.WriteVector3(msg.Anchor);
            writer.WriteInt(msg.SlotIndex);
            writer.WriteInt(msg.Token);
            writer.WriteUInt(msg.TargetNetId);
            writer.WriteVector3(msg.LocalPoint);
        }

        public static GrappleFireMessage ReadFire(NetworkReader reader) =>
            new GrappleFireMessage
            {
                Anchor = reader.ReadVector3(),
                SlotIndex = reader.ReadInt(),
                Token = reader.ReadInt(),
                TargetNetId = reader.ReadUInt(),
                LocalPoint = reader.ReadVector3(),
            };

        public static void WriteAnchor(NetworkWriter writer, GrappleAnchorMessage msg)
        {
            writer.WriteUInt(msg.PlayerNetId);
            writer.WriteVector3(msg.Anchor);
            writer.WriteInt(msg.Token);
            writer.WriteInt(msg.Generation);
            writer.WriteUInt(msg.TargetNetId);
            writer.WriteVector3(msg.LocalPoint);
        }

        public static GrappleAnchorMessage ReadAnchor(NetworkReader reader) =>
            new GrappleAnchorMessage
            {
                PlayerNetId = reader.ReadUInt(),
                Anchor = reader.ReadVector3(),
                Token = reader.ReadInt(),
                Generation = reader.ReadInt(),
                TargetNetId = reader.ReadUInt(),
                LocalPoint = reader.ReadVector3(),
            };

        public static void WriteReject(NetworkWriter writer, GrappleRejectMessage msg) =>
            writer.WriteInt(msg.Token);

        public static GrappleRejectMessage ReadReject(NetworkReader reader) =>
            new GrappleRejectMessage { Token = reader.ReadInt() };

        public static void WriteRelease(NetworkWriter writer, GrappleReleaseMessage msg) { }

        public static GrappleReleaseMessage ReadRelease(NetworkReader reader) =>
            new GrappleReleaseMessage();

        public static void WriteClear(NetworkWriter writer, GrappleClearMessage msg)
        {
            writer.WriteUInt(msg.PlayerNetId);
            writer.WriteInt(msg.Token);
            writer.WriteInt(msg.Generation);
        }

        public static GrappleClearMessage ReadClear(NetworkReader reader) =>
            new GrappleClearMessage
            {
                PlayerNetId = reader.ReadUInt(),
                Token = reader.ReadInt(),
                Generation = reader.ReadInt(),
            };
    }
}
