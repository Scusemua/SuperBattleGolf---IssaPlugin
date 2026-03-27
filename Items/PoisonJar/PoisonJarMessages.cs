using Mirror;
using UnityEngine;

namespace IssaPlugin.Items
{
    /// <summary>Client → Server: player threw the poison jar.</summary>
    public struct PoisonJarThrowMessage : NetworkMessage
    {
        public Vector3 ThrowOrigin;
        public Vector3 ThrowVelocity;
    }

    /// <summary>
    /// Server → All clients: jar has landed.
    /// Clients use this to spawn the local splash VFX and check whether the local
    /// player falls within the poison radius.
    /// </summary>
    public struct PoisonJarLandedMessage : NetworkMessage
    {
        public Vector3 Position;
        public float Radius;
        public float Duration;
        public uint ThrowerNetId;
    }

    public static class PoisonJarMessageSerialization
    {
        public static void WritePoisonJarThrowMessage(NetworkWriter w, PoisonJarThrowMessage m)
        {
            w.WriteVector3(m.ThrowOrigin);
            w.WriteVector3(m.ThrowVelocity);
        }

        public static PoisonJarThrowMessage ReadPoisonJarThrowMessage(NetworkReader r) =>
            new PoisonJarThrowMessage
            {
                ThrowOrigin = r.ReadVector3(),
                ThrowVelocity = r.ReadVector3(),
            };

        public static void WritePoisonJarLandedMessage(NetworkWriter w, PoisonJarLandedMessage m)
        {
            w.WriteVector3(m.Position);
            w.WriteFloat(m.Radius);
            w.WriteFloat(m.Duration);
            w.WriteUInt(m.ThrowerNetId);
        }

        public static PoisonJarLandedMessage ReadPoisonJarLandedMessage(NetworkReader r) =>
            new PoisonJarLandedMessage
            {
                Position = r.ReadVector3(),
                Radius = r.ReadFloat(),
                Duration = r.ReadFloat(),
                ThrowerNetId = r.ReadUInt(),
            };
    }
}
