using Mirror;
using UnityEngine;

namespace IssaPlugin.Items
{
    // ── Client → Server ──────────────────────────────────────────────────

    /// Sent when the local player activates the Placeable Wall item.
    /// Contains the camera ray used to determine placement position server-side.
    public struct PlaceWallMessage : NetworkMessage
    {
        public Vector3 RayOrigin;
        public Vector3 RayDirection;
        /// Extra clockwise yaw rotation (degrees) accumulated via the R key in the
        /// placement preview. Added on top of the camera-facing base rotation.
        public float ExtraYawDegrees;
    }

    public static class PlaceWallMessageSerialization
    {
        public static void WritePlaceWallMessage(NetworkWriter writer, PlaceWallMessage msg)
        {
            writer.WriteVector3(msg.RayOrigin);
            writer.WriteVector3(msg.RayDirection);
            writer.WriteFloat(msg.ExtraYawDegrees);
        }

        public static PlaceWallMessage ReadPlaceWallMessage(NetworkReader reader) =>
            new PlaceWallMessage
            {
                RayOrigin = reader.ReadVector3(),
                RayDirection = reader.ReadVector3(),
                ExtraYawDegrees = reader.ReadFloat(),
            };
    }

    // ── Server → All Clients ─────────────────────────────────────────────

    /// Broadcast when a wall is destroyed so all clients can play destruction VFX.
    public struct WallDestroyedMessage : NetworkMessage
    {
        public Vector3 DestroyPosition;
    }

    public static class WallDestroyedMessageSerialization
    {
        public static void WriteWallDestroyedMessage(
            NetworkWriter writer,
            WallDestroyedMessage msg
        )
        {
            writer.WriteVector3(msg.DestroyPosition);
        }

        public static WallDestroyedMessage ReadWallDestroyedMessage(NetworkReader reader) =>
            new WallDestroyedMessage { DestroyPosition = reader.ReadVector3() };
    }
}
