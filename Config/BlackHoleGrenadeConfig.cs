using BepInEx.Configuration;
using UnityEngine.InputSystem;

namespace IssaPlugin
{
    public class BlackHoleGrenadeConfig
    {
        private const string Section = "BlackHoleGrenade";

        public ConfigEntry<Key> GiveKey { get; private set; }
        public ConfigEntry<float> Uses { get; private set; }
        public ConfigEntry<float> ThrowSpeed { get; private set; }
        public ConfigEntry<float> MaxThrowSpeed { get; private set; }
        public ConfigEntry<float> LobAngle { get; private set; }
        public ConfigEntry<float> GraceTime { get; private set; }
        public ConfigEntry<float> SuckDuration { get; private set; }
        public ConfigEntry<float> SuckRadius { get; private set; }
        public ConfigEntry<float> SuckForce { get; private set; }
        public ConfigEntry<float> MaxSuckForce { get; private set; }
        public ConfigEntry<float> SpitForce { get; private set; }
        public ConfigEntry<float> SpitVfxScale { get; private set; }
        public ConfigEntry<float> KnockdownRadius { get; private set; }
        public ConfigEntry<bool> ExcludeThrower { get; private set; }
        public ConfigEntry<float> BonusGolfCartForceMultiplier { get; private set; }
        public ConfigEntry<bool> AffectsGolfBalls { get; private set; }
        public ConfigEntry<bool> ElectroShieldPreventsEffect { get; private set; }

        public BlackHoleGrenadeConfig(ConfigFile cfg, GlobalConfig global)
        {
            GiveKey = cfg.Bind(
                Section,
                "GiveKey",
                Key.Numpad7,
                "Debug key to add the Black Hole Grenade to your inventory."
            );
            Uses = cfg.Bind(Section, "Uses", 1f, "Number of uses per Black Hole Grenade pickup.");
            ThrowSpeed = cfg.Bind(Section, "ThrowSpeed", 20f, "Initial throw speed in m/s.");
            MaxThrowSpeed = cfg.Bind(
                Section,
                "MaxThrowSpeed",
                30f,
                "Server-side clamp on throw speed to prevent exploits."
            );
            LobAngle = cfg.Bind(
                Section,
                "LobAngle",
                0.4f,
                "Upward component added to the throw direction to create an arc. 0 = flat, 1 = 45° upward."
            );
            GraceTime = cfg.Bind(
                Section,
                "GraceTime",
                0.35f,
                "Seconds after throwing before the grenade can stick to the ground."
            );
            SuckDuration = cfg.Bind(
                Section,
                "SuckDuration",
                6f,
                "How many seconds the suction phase lasts before spitting."
            );
            SuckRadius = cfg.Bind(
                Section,
                "SuckRadius",
                35f,
                "Radius in units of the suction field."
            );
            SuckForce = cfg.Bind(
                Section,
                "SuckForce",
                8f,
                "Suction acceleration (m/s²) applied at the outer edge of the suction field."
            );
            MaxSuckForce = cfg.Bind(
                Section,
                "MaxSuckForce",
                40f,
                "Suction acceleration (m/s²) applied right at the center of the black hole."
            );
            SpitForce = cfg.Bind(
                Section,
                "SpitForce",
                35f,
                "Speed (m/s) at which objects and players are ejected during the spit phase."
            );
            SpitVfxScale = cfg.Bind(
                Section,
                "SpitVfxScale",
                3f,
                "Scale of the explosion VFX played on all clients when the black hole collapses."
            );
            KnockdownRadius = cfg.Bind(
                Section,
                "KnockdownRadius",
                15f,
                "Distance from the black hole at which a player gets knocked down. Must be less than SuckRadius. Set to 0 to disable."
            );
            ExcludeThrower = cfg.Bind(
                Section,
                "ExcludeThrower",
                true,
                "If true, the player who threw the grenade is not affected by suction or spit."
            );
            BonusGolfCartForceMultiplier = cfg.Bind(
                Section,
                "BonusGolfCartForceMultiplier",
                2.0f,
                "Additional force multiplier applied to golf carts."
            );
            AffectsGolfBalls = cfg.Bind(
                Section,
                "AffectsGolfBalls",
                true,
                "If true, the black hole grenade sucks in and spits out golf balls. Set to false to leave golf balls unaffected."
            );
            ElectroShieldPreventsEffect = cfg.Bind(
                Section,
                "ElectroShieldPreventsEffect",
                true,
                "If true, a player with an active electro shield is immune to the black hole's suction pull and spit ejection."
            );
        }
    }
}
