using Mirror;

namespace IssaPlugin.Network
{
    public struct SpawnWeightsMessage : NetworkMessage
    {
        public float Bat;
        public float Bomber;
        public float Missile;
        public float AC130;
        public float Freeze;
        public float LowGravity;
        public float Sniper;
        public float Donut;
    }

    public static class SpawnWeightsMessageSerialization
    {
        public static void WriteSpawnWeightsMessage(NetworkWriter writer, SpawnWeightsMessage msg)
        {
            writer.WriteFloat(msg.Bat);
            writer.WriteFloat(msg.Bomber);
            writer.WriteFloat(msg.Missile);
            writer.WriteFloat(msg.AC130);
            writer.WriteFloat(msg.Freeze);
            writer.WriteFloat(msg.LowGravity);
            writer.WriteFloat(msg.Sniper);
            writer.WriteFloat(msg.Donut);
        }

        public static SpawnWeightsMessage ReadSpawnWeightsMessage(NetworkReader reader)
        {
            return new SpawnWeightsMessage
            {
                Bat = reader.ReadFloat(),
                Bomber = reader.ReadFloat(),
                Missile = reader.ReadFloat(),
                AC130 = reader.ReadFloat(),
                Freeze = reader.ReadFloat(),
                LowGravity = reader.ReadFloat(),
                Sniper = reader.ReadFloat(),
                Donut = reader.ReadFloat(),
            };
        }
    }
}
