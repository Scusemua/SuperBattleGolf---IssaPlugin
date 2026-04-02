using BepInEx.Configuration;
using UnityEngine.InputSystem;

namespace IssaPlugin
{
    public class StickyGrenadeConfig
    {
        private const string Section = "StickyGrenade";

        public ConfigEntry<Key> GiveKey { get; private set; }
        public ConfigEntry<float> Uses { get; private set; }
        public ConfigEntry<float> SpawnWeight { get; private set; }
        public ConfigEntry<float> ThrowSpeed { get; private set; }
        public ConfigEntry<float> MaxThrowSpeed { get; private set; }
        public ConfigEntry<float> LobAngle { get; private set; }
        public ConfigEntry<float> FuseTime { get; private set; }
        public ConfigEntry<float> GraceTime { get; private set; }
        public ConfigEntry<float> StickRadius { get; private set; }
        public ConfigEntry<float> ExplosionScale { get; private set; }

        public StickyGrenadeConfig(ConfigFile cfg, GlobalConfig global)
        {
            GiveKey = cfg.Bind(Section, "GiveKey", Key.Numpad4, "Debug key to add the StickyGrenade grenade to your inventory.");
            Uses = cfg.Bind(Section, "Uses", 2f, "Number of StickyGrenade grenades per pickup.");
            ThrowSpeed = cfg.Bind(Section, "ThrowSpeed", 22f, "Initial speed in units per second when the grenade is thrown.");
            MaxThrowSpeed = cfg.Bind(Section, "MaxThrowSpeed", 35f, "Server-side cap on throw speed to prevent exploits.");
            LobAngle = cfg.Bind(Section, "LobAngle", 0.25f, "Upward component added to the throw direction for a natural lob arc. 0 = perfectly flat, 1 = 45 degrees upward.");
            FuseTime = cfg.Bind(Section, "FuseTime", 3.5f, "Seconds from when the grenade sticks until it detonates.");
            GraceTime = cfg.Bind(Section, "GraceTime", 0.35f, "Seconds after throwing before the grenade can stick to anything. Prevents the grenade from immediately sticking to the thrower.");
            StickRadius = cfg.Bind(Section, "StickRadius", 0.55f, "Radius of the overlap sphere used to detect stick targets each FixedUpdate.");

            SpawnWeight = global.BindSpawnWeight(cfg, 109, "StickyGrenadeWeight", 12f, "Override spawn weight for the Sticky Grenade.");
            ExplosionScale = cfg.Bind("Explosions", "StickyGrenadeScale", 4.0f, "Multiplier for StickyGrenade explosions. Affects blast radius, knockback, and VFX size.");
        }
    }
}
