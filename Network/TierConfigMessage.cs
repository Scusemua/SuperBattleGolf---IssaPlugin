// TierConfigMessage.cs
// Carries a full SpawnConfigSnapshot from host → all clients.
// Sent by TierConfigSyncer whenever the host makes a change via SpawnConfigUI.
//
// Wire format (all fields written in order):
//   bool   CustomItemSpawnsEnabled
//   float  GlobalSpawnRateMultiplier
//   float  CatchupBoostFactor
//   int    tierCount
//   per-tier block (tierCount times):
//       int    tier
//       float  spawnWeight
//       bool   tierEnabled
//       float  minDistanceBehindLeader
//       int    minPlaceTrigger
//   int    itemOverrideCount
//   per-item block (itemOverrideCount times):
//       int    itemTypeId
//       bool   enabled
//       bool   spawnWeightOverrideEnabled
//       float  spawnWeightOverride
//       int    assignedTier

using System.Collections.Generic;
using Mirror;

namespace IssaPlugin.Network
{
    public struct TierConfigMessage : NetworkMessage
    {
        public bool CustomItemSpawnsEnabled;
        public float GlobalSpawnRateMultiplier;
        public float CatchupBoostFactor;

        // Tier settings (variable length)
        public int[] TierNumbers;
        public float[] TierSpawnWeights;
        public bool[] TierEnabled;
        public float[] TierMinDistanceBehindLeader;
        public int[] TierMinPlaceTrigger;

        // Per-item overrides (variable length)
        public int[] ItemTypeIds;
        public bool[] ItemEnabled;
        public bool[] ItemSpawnWeightOverrideEnabled;
        public float[] ItemSpawnWeightOverride;
        public int[] ItemAssignedTier;
    }

    public static class TierConfigMessageSerialization
    {
        public static void WriteTierConfigMessage(NetworkWriter writer, TierConfigMessage msg)
        {
            writer.WriteBool(msg.CustomItemSpawnsEnabled);
            writer.WriteFloat(msg.GlobalSpawnRateMultiplier);
            writer.WriteFloat(msg.CatchupBoostFactor);

            // Tiers
            int tierCount = msg.TierNumbers?.Length ?? 0;
            writer.WriteInt(tierCount);
            for (int i = 0; i < tierCount; i++)
            {
                writer.WriteInt(msg.TierNumbers[i]);
                writer.WriteFloat(msg.TierSpawnWeights[i]);
                writer.WriteBool(msg.TierEnabled[i]);
                writer.WriteFloat(msg.TierMinDistanceBehindLeader[i]);
                writer.WriteInt(msg.TierMinPlaceTrigger[i]);
            }

            // Items
            int itemCount = msg.ItemTypeIds?.Length ?? 0;
            writer.WriteInt(itemCount);
            for (int i = 0; i < itemCount; i++)
            {
                writer.WriteInt(msg.ItemTypeIds[i]);
                writer.WriteBool(msg.ItemEnabled[i]);
                writer.WriteBool(msg.ItemSpawnWeightOverrideEnabled[i]);
                writer.WriteFloat(msg.ItemSpawnWeightOverride[i]);
                writer.WriteInt(msg.ItemAssignedTier[i]);
            }
        }

        public static TierConfigMessage ReadTierConfigMessage(NetworkReader reader)
        {
            var msg = new TierConfigMessage
            {
                CustomItemSpawnsEnabled = reader.ReadBool(),
                GlobalSpawnRateMultiplier = reader.ReadFloat(),
                CatchupBoostFactor = reader.ReadFloat(),
            };

            int tierCount = reader.ReadInt();
            msg.TierNumbers = new int[tierCount];
            msg.TierSpawnWeights = new float[tierCount];
            msg.TierEnabled = new bool[tierCount];
            msg.TierMinDistanceBehindLeader = new float[tierCount];
            msg.TierMinPlaceTrigger = new int[tierCount];

            for (int i = 0; i < tierCount; i++)
            {
                msg.TierNumbers[i] = reader.ReadInt();
                msg.TierSpawnWeights[i] = reader.ReadFloat();
                msg.TierEnabled[i] = reader.ReadBool();
                msg.TierMinDistanceBehindLeader[i] = reader.ReadFloat();
                msg.TierMinPlaceTrigger[i] = reader.ReadInt();
            }

            int itemCount = reader.ReadInt();
            msg.ItemTypeIds = new int[itemCount];
            msg.ItemEnabled = new bool[itemCount];
            msg.ItemSpawnWeightOverrideEnabled = new bool[itemCount];
            msg.ItemSpawnWeightOverride = new float[itemCount];
            msg.ItemAssignedTier = new int[itemCount];
            for (int i = 0; i < itemCount; i++)
            {
                msg.ItemTypeIds[i] = reader.ReadInt();
                msg.ItemEnabled[i] = reader.ReadBool();
                msg.ItemSpawnWeightOverrideEnabled[i] = reader.ReadBool();
                msg.ItemSpawnWeightOverride[i] = reader.ReadFloat();
                msg.ItemAssignedTier[i] = reader.ReadInt();
            }

            return msg;
        }

        // ─── Conversion helpers ────────────────────────────────────────────────

        /// <summary>
        /// Pack a <see cref="SpawnConfigSnapshot"/> into a wire-ready message.
        /// </summary>
        public static TierConfigMessage FromSnapshot(SpawnConfigSnapshot snap)
        {
            var msg = new TierConfigMessage
            {
                CustomItemSpawnsEnabled = snap.CustomItemSpawnsEnabled,
                GlobalSpawnRateMultiplier = snap.GlobalSpawnRateMultiplier,
                CatchupBoostFactor = snap.CatchupBoostFactor,
            };

            // Tiers
            var tiers = new List<IssaPlugin.TierSettings>(snap.TierSettings.Values);
            msg.TierNumbers = new int[tiers.Count];
            msg.TierSpawnWeights = new float[tiers.Count];
            msg.TierEnabled = new bool[tiers.Count];
            msg.TierMinDistanceBehindLeader = new float[tiers.Count];
            msg.TierMinPlaceTrigger = new int[tiers.Count];
            for (int i = 0; i < tiers.Count; i++)
            {
                msg.TierNumbers[i] = tiers[i].Tier;
                msg.TierSpawnWeights[i] = tiers[i].SpawnWeight;
                msg.TierEnabled[i] = tiers[i].TierEnabled;
                msg.TierMinDistanceBehindLeader[i] = tiers[i].MinDistanceBehindLeader;
                msg.TierMinPlaceTrigger[i] = tiers[i].MinPlaceTrigger;
            }

            // Items
            var items = new List<IssaPlugin.ItemOverrideSettings>(snap.ItemOverrides.Values);
            msg.ItemTypeIds = new int[items.Count];
            msg.ItemEnabled = new bool[items.Count];
            msg.ItemSpawnWeightOverrideEnabled = new bool[items.Count];
            msg.ItemSpawnWeightOverride = new float[items.Count];
            msg.ItemAssignedTier = new int[items.Count];
            for (int i = 0; i < items.Count; i++)
            {
                msg.ItemTypeIds[i] = items[i].ItemTypeId;
                msg.ItemEnabled[i] = items[i].Enabled;
                msg.ItemSpawnWeightOverrideEnabled[i] = items[i].SpawnWeightOverrideEnabled;
                msg.ItemSpawnWeightOverride[i] = items[i].SpawnWeightOverride;
                msg.ItemAssignedTier[i] = items[i].AssignedTier;
            }

            return msg;
        }

        /// <summary>
        /// Unpack a wire message into a <see cref="SpawnConfigSnapshot"/>.
        /// Called on non-host clients after receiving the message.
        /// </summary>
        public static SpawnConfigSnapshot ToSnapshot(TierConfigMessage msg)
        {
            var snap = new SpawnConfigSnapshot
            {
                CustomItemSpawnsEnabled = msg.CustomItemSpawnsEnabled,
                GlobalSpawnRateMultiplier = msg.GlobalSpawnRateMultiplier,
                CatchupBoostFactor = msg.CatchupBoostFactor,
            };

            int tierCount = msg.TierNumbers?.Length ?? 0;
            for (int i = 0; i < tierCount; i++)
            {
                int tier = msg.TierNumbers[i];
                snap.TierSettings[tier] = new IssaPlugin.TierSettings(tier, msg.TierSpawnWeights[i])
                {
                    TierEnabled = msg.TierEnabled[i],
                    MinDistanceBehindLeader = msg.TierMinDistanceBehindLeader[i],
                    MinPlaceTrigger = msg.TierMinPlaceTrigger[i],
                };
            }

            int itemCount = msg.ItemTypeIds?.Length ?? 0;
            for (int i = 0; i < itemCount; i++)
            {
                int id = msg.ItemTypeIds[i];
                snap.ItemOverrides[id] = new IssaPlugin.ItemOverrideSettings(id)
                {
                    Enabled = msg.ItemEnabled[i],
                    SpawnWeightOverrideEnabled = msg.ItemSpawnWeightOverrideEnabled[i],
                    SpawnWeightOverride = msg.ItemSpawnWeightOverride[i],
                    AssignedTier = msg.ItemAssignedTier[i],
                };
            }

            return snap;
        }
    }
}
