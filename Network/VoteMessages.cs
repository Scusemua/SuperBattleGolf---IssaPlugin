using System.Collections.Generic;
using Mirror;

namespace IssaPlugin.Network
{
    // Server → All: a vote session has started
    public struct VoteStartMessage : NetworkMessage
    {
        public float TimeoutSeconds;
    }

    // Client → Server: local player's votes
    public struct VoteSubmitMessage : NetworkMessage
    {
        public Dictionary<int, bool> Votes; // ItemType (int) → enabled
    }

    // Server → All: final tallied results
    public struct VoteResultsMessage : NetworkMessage
    {
        public Dictionary<int, bool> Results; // ItemType (int) → enabled
    }

    public static class VoteMessageSerialization
    {
        public static void WriteVoteStartMessage(NetworkWriter writer, VoteStartMessage msg) =>
            writer.WriteFloat(msg.TimeoutSeconds);

        public static VoteStartMessage ReadVoteStartMessage(NetworkReader reader) =>
            new VoteStartMessage { TimeoutSeconds = reader.ReadFloat() };

        public static void WriteVoteSubmitMessage(NetworkWriter writer, VoteSubmitMessage msg)
        {
            writer.WriteInt(msg.Votes.Count);
            foreach (var (itemType, enabled) in msg.Votes)
            {
                writer.WriteInt(itemType);
                writer.WriteBool(enabled);
            }
        }

        public static VoteSubmitMessage ReadVoteSubmitMessage(NetworkReader reader)
        {
            int count = reader.ReadInt();
            var votes = new Dictionary<int, bool>(count);
            for (int i = 0; i < count; i++)
                votes.Add(reader.ReadInt(), reader.ReadBool());
            return new VoteSubmitMessage { Votes = votes };
        }

        public static void WriteVoteResultsMessage(NetworkWriter writer, VoteResultsMessage msg)
        {
            writer.WriteInt(msg.Results.Count);
            foreach (var (itemType, enabled) in msg.Results)
            {
                writer.WriteInt(itemType);
                writer.WriteBool(enabled);
            }
        }

        public static VoteResultsMessage ReadVoteResultsMessage(NetworkReader reader)
        {
            int count = reader.ReadInt();
            var results = new Dictionary<int, bool>(count);
            for (int i = 0; i < count; i++)
                results.Add(reader.ReadInt(), reader.ReadBool());
            return new VoteResultsMessage { Results = results };
        }
    }
}
