using Mirror;
using UnityEngine;

namespace IssaPlugin.Items
{
    public struct AC130SoundMessage : NetworkMessage { }

    public static class AC130SoundMessageSerialization
    {
        public static void WriteAC130SoundMessage(NetworkWriter writer, AC130SoundMessage msg) { }

        public static AC130SoundMessage ReadAC130SoundMessage(NetworkReader reader)
        {
            return new AC130SoundMessage { };
        }
    }

    public struct AC130MaydayVfxMessage : NetworkMessage
    {
        public uint GunshipNetId;

        public override string ToString()
        {
            return $"AC130MaydayVfxMessage[GunshipNetId={GunshipNetId}]";
        }
    }

    public struct AC130MaydayImpactMessage : NetworkMessage
    {
        public Vector3 ImpactPos;

        public override string ToString()
        {
            return $"AC130MaydayImpactMessage[ImpactPos={ImpactPos}]";
        }
    }

    public static class AC130MaydayVfxMessageSerialization
    {
        public static void WriteAC130MaydayVfxMessage(
            NetworkWriter writer,
            AC130MaydayVfxMessage msg
        )
        {
            writer.WriteUInt(msg.GunshipNetId);
        }

        public static AC130MaydayVfxMessage ReadAC130MaydayVfxMessage(NetworkReader reader)
        {
            return new AC130MaydayVfxMessage { GunshipNetId = reader.ReadUInt() };
        }
    }

    public static class AC130MaydayImpactMessageSerialization
    {
        public static void WriteAC130MaydayImpactMessage(
            NetworkWriter writer,
            AC130MaydayImpactMessage msg
        )
        {
            writer.WriteVector3(msg.ImpactPos);
        }

        public static AC130MaydayImpactMessage ReadAC130MaydayImpactMessage(NetworkReader reader)
        {
            return new AC130MaydayImpactMessage { ImpactPos = reader.ReadVector3() };
        }
    }
}
