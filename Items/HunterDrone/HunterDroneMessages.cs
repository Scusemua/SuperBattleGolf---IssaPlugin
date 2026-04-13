using Mirror;
using UnityEngine;

namespace IssaPlugin.Items
{
    // ── Client → Server ──────────────────────────────────────────────────────

    /// <summary>Sent when the local player activates the Hunter Drone item.</summary>
    public struct HunterDroneLaunchMessage : NetworkMessage { }

    public static class HunterDroneLaunchMessageSerialization
    {
        public static void WriteHunterDroneLaunchMessage(
            NetworkWriter writer,
            HunterDroneLaunchMessage msg
        ) { }

        public static HunterDroneLaunchMessage ReadHunterDroneLaunchMessage(
            NetworkReader reader
        ) => new HunterDroneLaunchMessage();
    }

    /// <summary>
    /// Sent by a client to the server when a bullet raycast hit the hunter drone.
    /// The server uses <see cref="DroneNetId"/> to find and detonate the correct drone.
    /// </summary>
    public struct HunterDroneShotMessage : NetworkMessage
    {
        public uint DroneNetId;
    }

    public static class HunterDroneShotMessageSerialization
    {
        public static void WriteHunterDroneShotMessage(
            NetworkWriter writer,
            HunterDroneShotMessage msg
        ) => writer.WriteUInt(msg.DroneNetId);

        public static HunterDroneShotMessage ReadHunterDroneShotMessage(NetworkReader reader) =>
            new HunterDroneShotMessage { DroneNetId = reader.ReadUInt() };
    }

    // ── Server → All Clients ─────────────────────────────────────────────────

    /// <summary>
    /// Broadcast by the server the moment the hunter drone detonates.
    /// Every client spawns the drone explosion VFX at <see cref="Position"/>.
    /// </summary>
    public struct HunterDroneExplodedMessage : NetworkMessage
    {
        public Vector3 Position;
    }

    public static class HunterDroneExplodedMessageSerialization
    {
        public static void WriteHunterDroneExplodedMessage(
            NetworkWriter writer,
            HunterDroneExplodedMessage msg
        ) => writer.WriteVector3(msg.Position);

        public static HunterDroneExplodedMessage ReadHunterDroneExplodedMessage(
            NetworkReader reader
        ) => new HunterDroneExplodedMessage { Position = reader.ReadVector3() };
    }
}
