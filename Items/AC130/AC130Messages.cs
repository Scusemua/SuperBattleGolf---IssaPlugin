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

    // ── [Command] replacements (client→server) ──────────────────────────────

    public struct AC130StartMessage : NetworkMessage { }

    public static class AC130StartMessageSerialization
    {
        public static void WriteAC130StartMessage(NetworkWriter writer, AC130StartMessage msg) { }

        public static AC130StartMessage ReadAC130StartMessage(NetworkReader reader) =>
            new AC130StartMessage();
    }

    public struct AC130EndMessage : NetworkMessage { }

    public static class AC130EndMessageSerialization
    {
        public static void WriteAC130EndMessage(NetworkWriter writer, AC130EndMessage msg) { }

        public static AC130EndMessage ReadAC130EndMessage(NetworkReader reader) =>
            new AC130EndMessage();
    }

    public struct AC130FireMessage : NetworkMessage
    {
        public Vector3 AimDirection;
    }

    public static class AC130FireMessageSerialization
    {
        public static void WriteAC130FireMessage(NetworkWriter writer, AC130FireMessage msg)
        {
            writer.WriteVector3(msg.AimDirection);
        }

        public static AC130FireMessage ReadAC130FireMessage(NetworkReader reader)
        {
            return new AC130FireMessage { AimDirection = reader.ReadVector3() };
        }
    }

    public struct AC130TriggerMaydayMessage : NetworkMessage { }

    public static class AC130TriggerMaydayMessageSerialization
    {
        public static void WriteAC130TriggerMaydayMessage(
            NetworkWriter writer,
            AC130TriggerMaydayMessage msg
        ) { }

        public static AC130TriggerMaydayMessage ReadAC130TriggerMaydayMessage(
            NetworkReader reader
        ) => new AC130TriggerMaydayMessage();
    }

    public struct AC130PrepareHomingMessage : NetworkMessage { }

    public static class AC130PrepareHomingMessageSerialization
    {
        public static void WriteAC130PrepareHomingMessage(
            NetworkWriter writer,
            AC130PrepareHomingMessage msg
        ) { }

        public static AC130PrepareHomingMessage ReadAC130PrepareHomingMessage(
            NetworkReader reader
        ) => new AC130PrepareHomingMessage();
    }

    public struct AC130MaydayInputMessage : NetworkMessage
    {
        public float DiveInfluence;
        public float RollInfluence;
    }

    public static class AC130MaydayInputMessageSerialization
    {
        public static void WriteAC130MaydayInputMessage(
            NetworkWriter writer,
            AC130MaydayInputMessage msg
        )
        {
            writer.WriteFloat(msg.DiveInfluence);
            writer.WriteFloat(msg.RollInfluence);
        }

        public static AC130MaydayInputMessage ReadAC130MaydayInputMessage(NetworkReader reader)
        {
            return new AC130MaydayInputMessage
            {
                DiveInfluence = reader.ReadFloat(),
                RollInfluence = reader.ReadFloat(),
            };
        }
    }

    // ── [TargetRpc] replacements (server→client) ────────────────────────────

    public struct AC130BeginClientMessage : NetworkMessage
    {
        public uint GunshipNetId;
        public Vector3 MapCentre;
    }

    public static class AC130BeginClientMessageSerialization
    {
        public static void WriteAC130BeginClientMessage(
            NetworkWriter writer,
            AC130BeginClientMessage msg
        )
        {
            writer.WriteUInt(msg.GunshipNetId);
            writer.WriteVector3(msg.MapCentre);
        }

        public static AC130BeginClientMessage ReadAC130BeginClientMessage(NetworkReader reader)
        {
            return new AC130BeginClientMessage
            {
                GunshipNetId = reader.ReadUInt(),
                MapCentre = reader.ReadVector3(),
            };
        }
    }

    public struct AC130EndClientMessage : NetworkMessage { }

    public static class AC130EndClientMessageSerialization
    {
        public static void WriteAC130EndClientMessage(
            NetworkWriter writer,
            AC130EndClientMessage msg
        ) { }

        public static AC130EndClientMessage ReadAC130EndClientMessage(NetworkReader reader) =>
            new AC130EndClientMessage();
    }

    public struct AC130BeginMaydayClientMessage : NetworkMessage
    {
        public uint GunshipNetId;
    }

    public static class AC130BeginMaydayClientMessageSerialization
    {
        public static void WriteAC130BeginMaydayClientMessage(
            NetworkWriter writer,
            AC130BeginMaydayClientMessage msg
        )
        {
            writer.WriteUInt(msg.GunshipNetId);
        }

        public static AC130BeginMaydayClientMessage ReadAC130BeginMaydayClientMessage(
            NetworkReader reader
        )
        {
            return new AC130BeginMaydayClientMessage { GunshipNetId = reader.ReadUInt() };
        }
    }

    public struct AC130EndMaydayClientMessage : NetworkMessage { }

    public static class AC130EndMaydayClientMessageSerialization
    {
        public static void WriteAC130EndMaydayClientMessage(
            NetworkWriter writer,
            AC130EndMaydayClientMessage msg
        ) { }

        public static AC130EndMaydayClientMessage ReadAC130EndMaydayClientMessage(
            NetworkReader reader
        ) => new AC130EndMaydayClientMessage();
    }

    public struct AC130BusyMessage : NetworkMessage { }

    public static class AC130BusyMessageSerialization
    {
        public static void WriteAC130BusyMessage(NetworkWriter writer, AC130BusyMessage msg) { }

        public static AC130BusyMessage ReadAC130BusyMessage(NetworkReader reader) =>
            new AC130BusyMessage();
    }
}
