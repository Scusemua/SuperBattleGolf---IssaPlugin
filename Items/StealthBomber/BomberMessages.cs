using Mirror;
using UnityEngine;

namespace IssaPlugin.Items
{
    public struct BomberVisualSpawnMessage : NetworkMessage
    {
        public Vector3 SpawnPos;
        public Vector3 ExitPos;
        public Vector3 Direction;
        public float Speed;

        public override string ToString()
        {
            return $"BomberVisualSpawnMessage[SpawnPos={SpawnPos},ExitPos={ExitPos},Direction={Direction},Speed={Speed}]";
        }
    }

    public struct BomberShotDownMessage : NetworkMessage
    {
        public Vector3 CrashDir;
        public float CrashSpeed;
        public Vector3 ImpactDir;
        public Vector3 TorqueImpulse;

        public override string ToString()
        {
            return $"BomberShotDownMessage[CrashDir={CrashDir},CrashSpeed={CrashSpeed},ImpactDir={ImpactDir},TorqueImpulse={TorqueImpulse}]";
        }
    }

    public static class BomberVisualSpawnMessageSerialization
    {
        public static void WriteBomberVisualSpawnMessage(
            NetworkWriter writer,
            BomberVisualSpawnMessage msg
        )
        {
            writer.WriteVector3(msg.SpawnPos);
            writer.WriteVector3(msg.ExitPos);
            writer.WriteVector3(msg.Direction);
            writer.WriteFloat(msg.Speed);
        }

        public static BomberVisualSpawnMessage ReadBomberVisualSpawnMessage(NetworkReader reader)
        {
            return new BomberVisualSpawnMessage
            {
                SpawnPos = reader.ReadVector3(),
                ExitPos = reader.ReadVector3(),
                Direction = reader.ReadVector3(),
                Speed = reader.ReadFloat(),
            };
        }
    }

    public static class BomberShotDownMessageSerialization
    {
        public static void WriteBomberShotDownMessage(
            NetworkWriter writer,
            BomberShotDownMessage msg
        )
        {
            writer.WriteVector3(msg.CrashDir);
            writer.WriteFloat(msg.CrashSpeed);
            writer.WriteVector3(msg.ImpactDir);
            writer.WriteVector3(msg.TorqueImpulse);
        }

        public static BomberShotDownMessage ReadBomberShotDownMessage(NetworkReader reader)
        {
            return new BomberShotDownMessage
            {
                CrashDir = reader.ReadVector3(),
                CrashSpeed = reader.ReadFloat(),
                ImpactDir = reader.ReadVector3(),
                TorqueImpulse = reader.ReadVector3(),
            };
        }
    }
}
