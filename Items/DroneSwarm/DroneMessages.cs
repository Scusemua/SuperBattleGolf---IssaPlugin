using Mirror;
using UnityEngine;

namespace IssaPlugin.Items
{
    // ── Client → Server ──────────────────────────────────────────────────────

    /// <summary>Sent when the local player activates the Drone Swarm item.</summary>
    public struct DroneSwarmSummonMessage : NetworkMessage { }

    public static class DroneSwarmSummonMessageSerialization
    {
        public static void WriteDroneSwarmSummonMessage(
            NetworkWriter writer,
            DroneSwarmSummonMessage msg
        ) { }

        public static DroneSwarmSummonMessage ReadDroneSwarmSummonMessage(
            NetworkReader reader
        ) => new DroneSwarmSummonMessage();
    }

    // ── Server → All Clients ─────────────────────────────────────────────────

    /// <summary>
    /// Broadcast by the server the moment a drone detonates.
    /// Every client spawns the custom drone explosion VFX at <see cref="Position"/>.
    /// </summary>
    public struct DroneExplodedMessage : NetworkMessage
    {
        public Vector3 Position;
    }

    public static class DroneExplodedMessageSerialization
    {
        public static void WriteDroneExplodedMessage(
            NetworkWriter writer,
            DroneExplodedMessage msg
        ) => writer.WriteVector3(msg.Position);

        public static DroneExplodedMessage ReadDroneExplodedMessage(NetworkReader reader) =>
            new DroneExplodedMessage { Position = reader.ReadVector3() };
    }

    /// <summary>
    /// Broadcast to all clients when the entire swarm session ends
    /// (all drones destroyed or session timed out).
    /// Reserved for future client-side audio / VFX cleanup.
    /// </summary>
    public struct DroneSwarmSessionEndedMessage : NetworkMessage { }

    public static class DroneSwarmSessionEndedMessageSerialization
    {
        public static void WriteDroneSwarmSessionEndedMessage(
            NetworkWriter writer,
            DroneSwarmSessionEndedMessage msg
        ) { }

        public static DroneSwarmSessionEndedMessage ReadDroneSwarmSessionEndedMessage(
            NetworkReader reader
        ) => new DroneSwarmSessionEndedMessage();
    }

    // ── Server → Owning Client only (connectionToClient.Send replacements) ──
    // These are sent via connectionToClient.Send() so only the summoning player
    // receives them — not every player in the session.

    /// <summary>
    /// Sent to the summoning client immediately after drones are spawned.
    /// Tells the client to show the drone HUD with the correct count.
    /// </summary>
    public struct DroneSwarmOverlayBeginMessage : NetworkMessage
    {
        public int DroneCount;
    }

    public static class DroneSwarmOverlayBeginMessageSerialization
    {
        public static void WriteDroneSwarmOverlayBeginMessage(
            NetworkWriter writer,
            DroneSwarmOverlayBeginMessage msg
        ) => writer.WriteInt(msg.DroneCount);

        public static DroneSwarmOverlayBeginMessage ReadDroneSwarmOverlayBeginMessage(
            NetworkReader reader
        ) => new DroneSwarmOverlayBeginMessage { DroneCount = reader.ReadInt() };
    }

    /// <summary>
    /// Sent to the summoning client each time one of their drones is destroyed.
    /// The client decrements the HUD counter on receipt.
    /// </summary>
    public struct DroneDiedClientMessage : NetworkMessage { }

    public static class DroneDiedClientMessageSerialization
    {
        public static void WriteDroneDiedClientMessage(
            NetworkWriter writer,
            DroneDiedClientMessage msg
        ) { }

        public static DroneDiedClientMessage ReadDroneDiedClientMessage(
            NetworkReader reader
        ) => new DroneDiedClientMessage();
    }

    /// <summary>Sent to the summoning client when the session ends.</summary>
    public struct DroneSwarmOverlayEndMessage : NetworkMessage { }

    public static class DroneSwarmOverlayEndMessageSerialization
    {
        public static void WriteDroneSwarmOverlayEndMessage(
            NetworkWriter writer,
            DroneSwarmOverlayEndMessage msg
        ) { }

        public static DroneSwarmOverlayEndMessage ReadDroneSwarmOverlayEndMessage(
            NetworkReader reader
        ) => new DroneSwarmOverlayEndMessage();
    }
}
