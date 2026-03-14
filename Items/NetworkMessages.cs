using Mirror;
using UnityEngine;

namespace IssaPlugin.Items
{
    // ── Freeze World ────────────────────────────────────────────────────────
    public struct FreezeBeginMessage  : NetworkMessage { public float Duration; }
    public struct FreezeEndMessage    : NetworkMessage { }

    // ── Low Gravity ──────────────────────────────────────────────────────────
    public struct LowGravityBeginMessage : NetworkMessage { public float Duration; }
    public struct LowGravityEndMessage   : NetworkMessage { }

    // ── Stealth Bomber ───────────────────────────────────────────────────────
    public struct BomberVisualSpawnMessage : NetworkMessage
    {
        public Vector3 SpawnPos;
        public Vector3 ExitPos;
        public Vector3 Direction;
        public float   Speed;
    }

    public struct BomberShotDownMessage : NetworkMessage
    {
        public Vector3 CrashDir;
        public float   CrashSpeed;
        public Vector3 ImpactDir;
        public Vector3 TorqueImpulse;
    }

    // ── AC130 ────────────────────────────────────────────────────────────────
    public struct AC130SoundMessage        : NetworkMessage { }
    public struct AC130MaydayVfxMessage    : NetworkMessage { public uint GunshipNetId; }
    public struct AC130MaydayImpactMessage : NetworkMessage { public Vector3 ImpactPos;  }
}
