using Mirror;

namespace IssaPlugin.Network
{
    public struct SpawnWeightsMessage : NetworkMessage
    {
        public bool CustomItemSpawnsEnabled;
        public float Bat;
        public float Bomber;
        public float Missile;
        public float AC130;
        public float Freeze;
        public float LowGravity;
        public float Sniper;
        public float Donut;
        public float Javelin;
        public float StickyGrenade;
        public float Bear;
        public float BlackHoleGrenade;
        public float Nuke;
        public float PlaceableWall;
    }

    public static class SpawnWeightsMessageSerialization
    {
        public static void WriteSpawnWeightsMessage(NetworkWriter writer, SpawnWeightsMessage msg)
        {
            writer.WriteBool(msg.CustomItemSpawnsEnabled);
            writer.WriteFloat(msg.Bat);
            writer.WriteFloat(msg.Bomber);
            writer.WriteFloat(msg.Missile);
            writer.WriteFloat(msg.AC130);
            writer.WriteFloat(msg.Freeze);
            writer.WriteFloat(msg.LowGravity);
            writer.WriteFloat(msg.Sniper);
            writer.WriteFloat(msg.Donut);
            writer.WriteFloat(msg.Javelin);
            writer.WriteFloat(msg.StickyGrenade);
            writer.WriteFloat(msg.Bear);
            writer.WriteFloat(msg.BlackHoleGrenade);
            writer.WriteFloat(msg.Nuke);
            writer.WriteFloat(msg.PlaceableWall);
        }

        public static SpawnWeightsMessage ReadSpawnWeightsMessage(NetworkReader reader)
        {
            return new SpawnWeightsMessage
            {
                CustomItemSpawnsEnabled = reader.ReadBool(),
                Bat = reader.ReadFloat(),
                Bomber = reader.ReadFloat(),
                Missile = reader.ReadFloat(),
                AC130 = reader.ReadFloat(),
                Freeze = reader.ReadFloat(),
                LowGravity = reader.ReadFloat(),
                Sniper = reader.ReadFloat(),
                Donut = reader.ReadFloat(),
                Javelin = reader.ReadFloat(),
                StickyGrenade = reader.ReadFloat(),
                Bear = reader.ReadFloat(),
                BlackHoleGrenade = reader.ReadFloat(),
                Nuke = reader.ReadFloat(),
                PlaceableWall = reader.ReadFloat(),
            };
        }
    }
}
