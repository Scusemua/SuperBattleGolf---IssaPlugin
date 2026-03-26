using Mirror;
using UnityEngine;

namespace IssaPlugin.Items
{
    /// Client → Server: initiator requests a position swap with a specific player.
    public struct PositionSwapRequestMessage : NetworkMessage
    {
        public uint TargetNetId;
    }

    public static class PositionSwapRequestMessageSerialization
    {
        public static void WritePositionSwapRequestMessage(
            NetworkWriter writer,
            PositionSwapRequestMessage msg
        )
        {
            writer.WriteUInt(msg.TargetNetId);
        }

        public static PositionSwapRequestMessage ReadPositionSwapRequestMessage(
            NetworkReader reader
        )
        {
            return new PositionSwapRequestMessage { TargetNetId = reader.ReadUInt() };
        }
    }

    /// Server → All clients: a swap is pending; spawn warning orbs.
    public struct PositionSwapWarningMessage : NetworkMessage
    {
        public uint InitiatorNetId;
        public uint TargetNetId;
        public float Delay;
    }

    public static class PositionSwapWarningMessageSerialization
    {
        public static void WritePositionSwapWarningMessage(
            NetworkWriter writer,
            PositionSwapWarningMessage msg
        )
        {
            writer.WriteUInt(msg.InitiatorNetId);
            writer.WriteUInt(msg.TargetNetId);
            writer.WriteFloat(msg.Delay);
        }

        public static PositionSwapWarningMessage ReadPositionSwapWarningMessage(
            NetworkReader reader
        )
        {
            return new PositionSwapWarningMessage
            {
                InitiatorNetId = reader.ReadUInt(),
                TargetNetId = reader.ReadUInt(),
                Delay = reader.ReadFloat(),
            };
        }
    }

    /// Server → Specific client only: teleport your local player to this position.
    public struct PositionSwapTeleportMessage : NetworkMessage
    {
        public Vector3 NewPosition;
    }

    public static class PositionSwapTeleportMessageSerialization
    {
        public static void WritePositionSwapTeleportMessage(
            NetworkWriter writer,
            PositionSwapTeleportMessage msg
        )
        {
            writer.WriteVector3(msg.NewPosition);
        }

        public static PositionSwapTeleportMessage ReadPositionSwapTeleportMessage(
            NetworkReader reader
        )
        {
            return new PositionSwapTeleportMessage { NewPosition = reader.ReadVector3() };
        }
    }

    /// Server → All clients: swap executed — destroy orbs, spawn smoke VFX.
    public struct PositionSwapExecuteMessage : NetworkMessage
    {
        public uint InitiatorNetId;
        public uint TargetNetId;
        // Both "new" positions are the swapped destinations, included so every client
        // can spawn smoke at the correct world-space locations regardless of latency.
        public Vector3 InitiatorDestination;
        public Vector3 TargetDestination;
    }

    public static class PositionSwapExecuteMessageSerialization
    {
        public static void WritePositionSwapExecuteMessage(
            NetworkWriter writer,
            PositionSwapExecuteMessage msg
        )
        {
            writer.WriteUInt(msg.InitiatorNetId);
            writer.WriteUInt(msg.TargetNetId);
            writer.WriteVector3(msg.InitiatorDestination);
            writer.WriteVector3(msg.TargetDestination);
        }

        public static PositionSwapExecuteMessage ReadPositionSwapExecuteMessage(
            NetworkReader reader
        )
        {
            return new PositionSwapExecuteMessage
            {
                InitiatorNetId = reader.ReadUInt(),
                TargetNetId = reader.ReadUInt(),
                InitiatorDestination = reader.ReadVector3(),
                TargetDestination = reader.ReadVector3(),
            };
        }
    }

    public enum PositionSwapCancelReason : byte
    {
        Generic = 0,
        PlayerEnteredGolfCart = 1,
    }

    /// Server → All clients: swap was cancelled before it could execute.
    /// Broadcast so every client can clean up warning orbs; clients check InitiatorNetId
    /// to decide whether to close the chooser/countdown overlay.
    public struct PositionSwapCancelledMessage : NetworkMessage
    {
        public uint InitiatorNetId;
        public PositionSwapCancelReason CancelReason;
    }

    public static class PositionSwapCancelledMessageSerialization
    {
        public static void WritePositionSwapCancelledMessage(
            NetworkWriter writer,
            PositionSwapCancelledMessage msg
        )
        {
            writer.WriteUInt(msg.InitiatorNetId);
            writer.WriteByte((byte)msg.CancelReason);
        }

        public static PositionSwapCancelledMessage ReadPositionSwapCancelledMessage(
            NetworkReader reader
        )
        {
            return new PositionSwapCancelledMessage
            {
                InitiatorNetId = reader.ReadUInt(),
                CancelReason = (PositionSwapCancelReason)reader.ReadByte(),
            };
        }
    }
}
