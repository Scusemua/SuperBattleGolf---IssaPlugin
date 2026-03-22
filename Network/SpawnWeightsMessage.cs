using System.Collections.Generic;
using IssaPlugin.Items;
using Mirror;

namespace IssaPlugin.Network
{
    public struct SpawnWeightsMessage : NetworkMessage
    {
        public bool CustomItemSpawnsEnabled;

        public Dictionary<int, float> ItemSpawnWeights;

        public readonly string ToString(SpawnWeightsMessage msg)
        {
            string s = $"CustomItemSpawnsEnabled={CustomItemSpawnsEnabled} ";
            foreach (var (itemType, spawnWeight) in msg.ItemSpawnWeights)
            {
                CustomItemDefinition itemDef = ItemRegistry.CustomItemDefinitionMap[
                    itemType
                ];
                s += $"{itemDef.DisplayName}={msg.ItemSpawnWeights[itemType]}";
            }

            return s;
        }
    }

    public static class SpawnWeightsMessageSerialization
    {
        public static void WriteSpawnWeightsMessage(NetworkWriter writer, SpawnWeightsMessage msg)
        {
            writer.WriteBool(msg.CustomItemSpawnsEnabled);
            writer.WriteInt(msg.ItemSpawnWeights.Count);
            foreach (var (itemType, spawnWeight) in msg.ItemSpawnWeights)
            {
                writer.WriteInt(itemType);
                writer.WriteFloat(spawnWeight);
            }
        }

        public static SpawnWeightsMessage ReadSpawnWeightsMessage(NetworkReader reader)
        {
            bool customItemSpawnsEnabled = reader.ReadBool();

            int spawnWeightCount = reader.ReadInt();
            Dictionary<int, float> itemSpawnWeights = new Dictionary<int, float>(spawnWeightCount);

            for (int i = 0; i < spawnWeightCount; i++)
            {
                itemSpawnWeights.Add(reader.ReadInt(), reader.ReadFloat());
            }

            return new SpawnWeightsMessage
            {
                CustomItemSpawnsEnabled = customItemSpawnsEnabled,
                ItemSpawnWeights = itemSpawnWeights,
            };
        }
    }
}
