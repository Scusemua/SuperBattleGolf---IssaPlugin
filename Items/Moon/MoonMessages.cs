using Mirror;
using UnityEngine;

namespace IssaPlugin.Items
{
    // ── Client → Server ──────────────────────────────────────────────────────

    /// Sent when the local player uses the Moon item. No payload needed — the server
    /// identifies the wielder via the connection.
    public struct MoonUseMessage : NetworkMessage { }

    public static class MoonUseMessageSerialization
    {
        public static void WriteMoonUseMessage(NetworkWriter writer, MoonUseMessage msg) { }

        public static MoonUseMessage ReadMoonUseMessage(NetworkReader reader) =>
            new MoonUseMessage();
    }

    // ── Server → All Clients ─────────────────────────────────────────────────

    /// Broadcast when the moon session begins. All clients use this to spawn and
    /// animate the moon VFX and apply physics to the local player.
    /// StartTime is NOT included — each client records Time.time on receipt.
    public struct MoonBeginMessage : NetworkMessage
    {
        public uint WielderNetId;

        /// Moon spawns far away at this position.
        public Vector3 MoonSpawnPos;

        /// Moon hovers here during the suck phase (above the hole).
        public Vector3 MoonImpactPos;

        public float ApproachDuration;
        public float SuckDuration;
        public float InitialScale;
        public float FinalScale;
    }

    public static class MoonBeginMessageSerialization
    {
        public static void WriteMoonBeginMessage(NetworkWriter writer, MoonBeginMessage msg)
        {
            writer.WriteUInt(msg.WielderNetId);
            writer.WriteVector3(msg.MoonSpawnPos);
            writer.WriteVector3(msg.MoonImpactPos);
            writer.WriteFloat(msg.ApproachDuration);
            writer.WriteFloat(msg.SuckDuration);
            writer.WriteFloat(msg.InitialScale);
            writer.WriteFloat(msg.FinalScale);
        }

        public static MoonBeginMessage ReadMoonBeginMessage(NetworkReader reader) =>
            new MoonBeginMessage
            {
                WielderNetId = reader.ReadUInt(),
                MoonSpawnPos = reader.ReadVector3(),
                MoonImpactPos = reader.ReadVector3(),
                ApproachDuration = reader.ReadFloat(),
                SuckDuration = reader.ReadFloat(),
                InitialScale = reader.ReadFloat(),
                FinalScale = reader.ReadFloat(),
            };
    }

    /// Broadcast when the moon session ends. Triggers explosion VFX and cleanup.
    public struct MoonEndMessage : NetworkMessage
    {
        /// Used to key into the client session dictionary.
        public uint WielderNetId;
        public Vector3 ExplosionPos;
    }

    public static class MoonEndMessageSerialization
    {
        public static void WriteMoonEndMessage(NetworkWriter writer, MoonEndMessage msg)
        {
            writer.WriteUInt(msg.WielderNetId);
            writer.WriteVector3(msg.ExplosionPos);
        }

        public static MoonEndMessage ReadMoonEndMessage(NetworkReader reader) =>
            new MoonEndMessage
            {
                WielderNetId = reader.ReadUInt(),
                ExplosionPos = reader.ReadVector3(),
            };
    }
}
