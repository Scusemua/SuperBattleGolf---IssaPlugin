using Mirror;
using UnityEngine;

namespace IssaPlugin.Items
{
    // ── Client → Server ──────────────────────────────────────────────────

    /// Sent by the owning client when the player activates the Nuke.
    /// No payload needed — the server always drops it at the map centre.
    public struct NukeFireMessage : NetworkMessage { }

    public static class NukeFireMessageSerialization
    {
        public static void WriteNukeFireMessage(NetworkWriter w, NukeFireMessage m) { }

        public static NukeFireMessage ReadNukeFireMessage(NetworkReader r) => new NukeFireMessage();
    }

    // ── Server → All Clients ─────────────────────────────────────────────

    /// Broadcast to every client when the nuke bomb detonates so each
    /// client can spawn the explosion VFX and play the impact sound locally.
    public struct NukeExplosionMessage : NetworkMessage
    {
        public Vector3 Position;
    }

    public static class NukeExplosionMessageSerialization
    {
        public static void WriteNukeExplosionMessage(NetworkWriter w, NukeExplosionMessage m) =>
            w.WriteVector3(m.Position);

        public static NukeExplosionMessage ReadNukeExplosionMessage(NetworkReader r) =>
            new NukeExplosionMessage { Position = r.ReadVector3() };
    }
}
