using System.Collections.Generic;
using IssaPlugin.Items;
using Mirror;

namespace IssaPlugin.Network
{
    public struct SpawnWeightsMessage : NetworkMessage
    {
        public bool CustomItemSpawnsEnabled;

        // key: (int)ItemType, value: float[6] indexed by pool 0-5
        public Dictionary<int, float[]> ItemPoolWeights;

        public override string ToString()
        {
            if (ItemPoolWeights == null) return "SpawnWeightsMessage{null}";
            var sb = new System.Text.StringBuilder();
            sb.Append($"CustomItemSpawnsEnabled={CustomItemSpawnsEnabled} ");
            foreach (var (itemTypeId, weights) in ItemPoolWeights)
            {
                var def = ItemRegistry.GetDefinition((ItemType)itemTypeId);
                string name = def?.DisplayName ?? itemTypeId.ToString();
                sb.Append($"{name}=[");
                for (int p = 0; p < weights.Length; p++)
                {
                    if (p > 0) sb.Append(',');
                    sb.Append(weights[p].ToString("F1"));
                }
                sb.Append("] ");
            }
            return sb.ToString();
        }
    }

    public static class SpawnWeightsMessageSerialization
    {
        public static void WriteSpawnWeightsMessage(NetworkWriter writer, SpawnWeightsMessage msg)
        {
            writer.WriteBool(msg.CustomItemSpawnsEnabled);
            int count = msg.ItemPoolWeights?.Count ?? 0;
            writer.WriteInt(count);
            if (msg.ItemPoolWeights == null) return;
            foreach (var (itemTypeId, weights) in msg.ItemPoolWeights)
            {
                writer.WriteInt(itemTypeId);
                // Always write exactly 6 floats; pad with 0 if array is shorter.
                for (int p = 0; p < 6; p++)
                    writer.WriteFloat(p < weights.Length ? weights[p] : 0f);
            }
        }

        public static SpawnWeightsMessage ReadSpawnWeightsMessage(NetworkReader reader)
        {
            bool enabled = reader.ReadBool();
            int count = reader.ReadInt();
            var poolWeights = new Dictionary<int, float[]>(count);
            for (int i = 0; i < count; i++)
            {
                int itemTypeId = reader.ReadInt();
                var weights = new float[6];
                for (int p = 0; p < 6; p++)
                    weights[p] = reader.ReadFloat();
                poolWeights[itemTypeId] = weights;
            }
            return new SpawnWeightsMessage
            {
                CustomItemSpawnsEnabled = enabled,
                ItemPoolWeights = poolWeights,
            };
        }
    }
}
