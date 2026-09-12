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

        /// <summary>
        /// True when the shooter is riding this cart (Joyride mode). The server seats
        /// them in the driver seat and uses JoyrideLaunchSpeed instead of LaunchSpeed.
        /// </summary>
        public bool Joyride;
    }

    // ── Server → Owning Client ───────────────────────────────────────────────

    /// <summary>
    /// Tells the Joyride rider to apply the launch velocity to their own cart.
    ///
    /// Seating a REMOTE player in the driver seat hands them Mirror authority over the
    /// cart (GolfCartInfo.ServerTryAssignPassengerToSeat assigns client authority and
    /// flips the movement to ClientToServer). The server writing linearVelocity would
    /// then simply be overwritten by the owning client’s own physics, so the owner has
    /// to apply it instead. On a host-driven cart the server applies it directly and
    /// this message is never sent.
    /// </summary>
    public struct GolfCartJoyrideLaunchMessage : NetworkMessage
    {
        /// <summary>netId of the cart to launch.</summary>
        public uint CartNetId;

        /// <summary>World-space velocity to apply.</summary>
        public Vector3 Velocity;
    }

    // ── Serialization ────────────────────────────────────────────────────────

    public static class GolfCartLauncherMessageSerialization
    {
        public static void WriteLaunchRequest(NetworkWriter w, GolfCartLaunchRequestMessage msg)
        {
            w.WriteVector3(msg.Direction);
            w.WriteBool(msg.Joyride);
        }

        public static GolfCartLaunchRequestMessage ReadLaunchRequest(NetworkReader r) =>
            new() { Direction = r.ReadVector3(), Joyride = r.ReadBool() };

        public static void WriteJoyrideLaunch(NetworkWriter w, GolfCartJoyrideLaunchMessage msg)
        {
            w.WriteUInt(msg.CartNetId);
            w.WriteVector3(msg.Velocity);
        }

        public static GolfCartJoyrideLaunchMessage ReadJoyrideLaunch(NetworkReader r) =>
            new() { CartNetId = r.ReadUInt(), Velocity = r.ReadVector3() };
    }
}
