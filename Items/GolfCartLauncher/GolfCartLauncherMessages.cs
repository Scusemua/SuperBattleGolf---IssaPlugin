using Mirror;
using UnityEngine;

namespace IssaPlugin.Items
{
    // ── Client → Server ──────────────────────────────────────────────────────

    /// <summary>
    /// The local player fired the Golf Cart Launcher. The server validates the
    /// request, spawns a base-game golf cart and launches it along
    /// <see cref="Direction"/>.
    ///
    /// Only the direction is sent — the spawn position, speed and spin are all
    /// derived server-side from the shooter's networked transform and config, so a
    /// modified client cannot choose where the cart appears.
    /// </summary>
    public struct GolfCartLaunchRequestMessage : NetworkMessage
    {
        /// <summary>Normalized world-space direction to launch the cart along.</summary>
        public Vector3 Direction;
    }

    // ── Serialization ────────────────────────────────────────────────────────

    public static class GolfCartLauncherMessageSerialization
    {
        public static void WriteLaunchRequest(NetworkWriter w, GolfCartLaunchRequestMessage msg) =>
            w.WriteVector3(msg.Direction);

        public static GolfCartLaunchRequestMessage ReadLaunchRequest(NetworkReader r) =>
            new() { Direction = r.ReadVector3() };
    }
}
