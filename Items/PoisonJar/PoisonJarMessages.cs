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
    /// Clients spawn the splash VFX and a one-shot burst above each player in
    /// <see cref="PoisonedNetIds"/>. The local player applies the poison overlay
    /// when their net id is in that list.
    /// </summary>
    public struct PoisonJarLandedMessage : NetworkMessage
    {
        public Vector3 Position;
        public float Radius;
        public float Duration;
        public uint ThrowerNetId;

        /// Players the server poisoned. Shielded players are omitted.
        public uint[] PoisonedNetIds;
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

            uint[] ids = m.PoisonedNetIds;
            int count = ids != null ? ids.Length : 0;
            w.WriteInt(count);
            for (int i = 0; i < count; i++)
                w.WriteUInt(ids[i]);
        }

        public static PoisonJarLandedMessage ReadPoisonJarLandedMessage(NetworkReader r)
        {
            var message = new PoisonJarLandedMessage
            {
                Position = r.ReadVector3(),
                Radius = r.ReadFloat(),
                Duration = r.ReadFloat(),
                ThrowerNetId = r.ReadUInt(),
            };

            int count = r.ReadInt();
            message.PoisonedNetIds = new uint[count];
            for (int i = 0; i < count; i++)
                message.PoisonedNetIds[i] = r.ReadUInt();
            return message;
        }
    }
}
