using Mirror;
using UnityEngine;

namespace IssaPlugin.Items
{
    // ── Client → Server ───────────────────────────────────────────────────────

    /// <summary>Client activated the Iron Man suit.</summary>
    public struct IronManActivateMessage : NetworkMessage { }

    /// <summary>Client sent a flight input tick (direction vector in world space).</summary>
    public struct IronManFlightInputMessage : NetworkMessage
    {
        public Vector3 MoveDirection; // world-space, normalised; zero = hover
    }

    /// <summary>Client fired a wrist rocket toward AimDirection.</summary>
    public struct IronManFireMessage : NetworkMessage
    {
        public Vector3 AimDirection; // world-space unit vector from camera
    }

    // ── Server → All Clients ─────────────────────────────────────────────────

    /// <summary>Show suit VFX/prefab on a specific player.</summary>
    public struct IronManSuitBeginMessage : NetworkMessage
    {
        public uint PlayerNetId;
    }

    /// <summary>Hide suit VFX/prefab on a specific player.</summary>
    public struct IronManSuitEndMessage : NetworkMessage
    {
        public uint PlayerNetId;
    }

    /// <summary>Client → Server: local player started thrusting.</summary>
    public struct IronManThrusterBeginMessage : NetworkMessage { }

    /// <summary>Client → Server: local player stopped thrusting.</summary>
    public struct IronManThrusterEndMessage : NetworkMessage { }

    /// <summary>Server → All Clients: show thruster particles on a specific player.</summary>
    public struct IronManThrusterBroadcastBeginMessage : NetworkMessage
    {
        public uint PlayerNetId;
    }

    /// <summary>Server → All Clients: hide thruster particles on a specific player.</summary>
    public struct IronManThrusterBroadcastEndMessage : NetworkMessage
    {
        public uint PlayerNetId;
    }

    /// <summary>A wrist rocket was fired — spawn trail VFX on all clients.</summary>
    public struct IronManRocketFiredMessage : NetworkMessage
    {
        public Vector3 Origin;
        public Vector3 Direction;
    }

    // ── Server → Owning Client only ───────────────────────────────────────────

    /// <summary>Sends the server's config values to the wielder so local HUD is correct.</summary>
    public struct IronManConfigMessage : NetworkMessage
    {
        public float Duration;
        public int   MaxRockets;
        public float FlightSpeed;
        public float RocketExplosionScale;
    }

    /// <summary>Ammo count update sent to the wielder's HUD.</summary>
    public struct IronManAmmoMessage : NetworkMessage
    {
        public int RocketsRemaining;
    }

    public static class IronManMessageSerialization
    {
        // ── IronManActivateMessage ────────────────────────────────────────────
        public static void WriteActivate(NetworkWriter w, IronManActivateMessage msg) { }
        public static IronManActivateMessage ReadActivate(NetworkReader r) => new IronManActivateMessage();

        // ── IronManFlightInputMessage ─────────────────────────────────────────
        public static void WriteFlightInput(NetworkWriter w, IronManFlightInputMessage msg) =>
            w.WriteVector3(msg.MoveDirection);
        public static IronManFlightInputMessage ReadFlightInput(NetworkReader r) =>
            new IronManFlightInputMessage { MoveDirection = r.ReadVector3() };

        // ── IronManFireMessage ────────────────────────────────────────────────
        public static void WriteFire(NetworkWriter w, IronManFireMessage msg) =>
            w.WriteVector3(msg.AimDirection);
        public static IronManFireMessage ReadFire(NetworkReader r) =>
            new IronManFireMessage { AimDirection = r.ReadVector3() };

        // ── IronManSuitBeginMessage ───────────────────────────────────────────
        public static void WriteSuitBegin(NetworkWriter w, IronManSuitBeginMessage msg) =>
            w.WriteUInt(msg.PlayerNetId);
        public static IronManSuitBeginMessage ReadSuitBegin(NetworkReader r) =>
            new IronManSuitBeginMessage { PlayerNetId = r.ReadUInt() };

        // ── IronManSuitEndMessage ─────────────────────────────────────────────
        public static void WriteSuitEnd(NetworkWriter w, IronManSuitEndMessage msg) =>
            w.WriteUInt(msg.PlayerNetId);
        public static IronManSuitEndMessage ReadSuitEnd(NetworkReader r) =>
            new IronManSuitEndMessage { PlayerNetId = r.ReadUInt() };

        // ── IronManThrusterBeginMessage (client→server, no payload) ──────────
        public static void WriteThrusterBegin(NetworkWriter w, IronManThrusterBeginMessage msg) { }
        public static IronManThrusterBeginMessage ReadThrusterBegin(NetworkReader r) =>
            new IronManThrusterBeginMessage();

        // ── IronManThrusterEndMessage (client→server, no payload) ────────────
        public static void WriteThrusterEnd(NetworkWriter w, IronManThrusterEndMessage msg) { }
        public static IronManThrusterEndMessage ReadThrusterEnd(NetworkReader r) =>
            new IronManThrusterEndMessage();

        // ── IronManThrusterBroadcastBeginMessage (server→all clients) ─────────
        public static void WriteThrusterBroadcastBegin(NetworkWriter w, IronManThrusterBroadcastBeginMessage msg) =>
            w.WriteUInt(msg.PlayerNetId);
        public static IronManThrusterBroadcastBeginMessage ReadThrusterBroadcastBegin(NetworkReader r) =>
            new IronManThrusterBroadcastBeginMessage { PlayerNetId = r.ReadUInt() };

        // ── IronManThrusterBroadcastEndMessage (server→all clients) ───────────
        public static void WriteThrusterBroadcastEnd(NetworkWriter w, IronManThrusterBroadcastEndMessage msg) =>
            w.WriteUInt(msg.PlayerNetId);
        public static IronManThrusterBroadcastEndMessage ReadThrusterBroadcastEnd(NetworkReader r) =>
            new IronManThrusterBroadcastEndMessage { PlayerNetId = r.ReadUInt() };

        // ── IronManRocketFiredMessage ─────────────────────────────────────────
        public static void WriteRocketFired(NetworkWriter w, IronManRocketFiredMessage msg)
        {
            w.WriteVector3(msg.Origin);
            w.WriteVector3(msg.Direction);
        }
        public static IronManRocketFiredMessage ReadRocketFired(NetworkReader r) =>
            new IronManRocketFiredMessage { Origin = r.ReadVector3(), Direction = r.ReadVector3() };

        // ── IronManConfigMessage ──────────────────────────────────────────────
        public static void WriteConfig(NetworkWriter w, IronManConfigMessage msg)
        {
            w.WriteFloat(msg.Duration);
            w.WriteInt(msg.MaxRockets);
            w.WriteFloat(msg.FlightSpeed);
            w.WriteFloat(msg.RocketExplosionScale);
        }
        public static IronManConfigMessage ReadConfig(NetworkReader r) =>
            new IronManConfigMessage
            {
                Duration            = r.ReadFloat(),
                MaxRockets          = r.ReadInt(),
                FlightSpeed         = r.ReadFloat(),
                RocketExplosionScale = r.ReadFloat(),
            };

        // ── IronManAmmoMessage ────────────────────────────────────────────────
        public static void WriteAmmo(NetworkWriter w, IronManAmmoMessage msg) =>
            w.WriteInt(msg.RocketsRemaining);
        public static IronManAmmoMessage ReadAmmo(NetworkReader r) =>
            new IronManAmmoMessage { RocketsRemaining = r.ReadInt() };
    }
}
