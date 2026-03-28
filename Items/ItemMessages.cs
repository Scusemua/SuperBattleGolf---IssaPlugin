using Mirror;
using UnityEngine;

namespace IssaPlugin.Items
{
    // ── Server → All Clients ─────────────────────────────────────────────

    /// Broadcast by the server when a scaled custom-item rocket explodes.
    /// Clients use it to render a larger explosion VFX visible to everyone.
    public struct ScaledExplosionVfxMessage : NetworkMessage
    {
        public Vector3 Position;
        public float Scale;
    }

    public static class ScaledExplosionVfxMessageSerialization
    {
        public static void WriteScaledExplosionVfxMessage(
            NetworkWriter writer,
            ScaledExplosionVfxMessage msg
        )
        {
            writer.WriteVector3(msg.Position);
            writer.WriteFloat(msg.Scale);
        }

        public static ScaledExplosionVfxMessage ReadScaledExplosionVfxMessage(NetworkReader reader)
        {
            return new ScaledExplosionVfxMessage
            {
                Position = reader.ReadVector3(),
                Scale = reader.ReadFloat(),
            };
        }
    }

    // ── Client → Server ──────────────────────────────────────────────────

    /// Sent by a non-host client when a hotkey is pressed to give themselves an item.
    /// The server checks AllowHotkeyItemGiving before granting the item.
    /// The host bypasses this message entirely and adds items directly.
    public struct GiveItemRequestMessage : NetworkMessage
    {
        public ItemType ItemType;
        public int Uses;
    }

    public static class GiveItemRequestMessageSerialization
    {
        public static void WriteGiveItemRequestMessage(
            NetworkWriter writer,
            GiveItemRequestMessage msg
        )
        {
            writer.WriteInt((int)msg.ItemType);
            writer.WriteInt(msg.Uses);
        }

        public static GiveItemRequestMessage ReadGiveItemRequestMessage(NetworkReader reader)
        {
            return new GiveItemRequestMessage
            {
                ItemType = (ItemType)reader.ReadInt(),
                Uses = reader.ReadInt(),
            };
        }
    }
}
