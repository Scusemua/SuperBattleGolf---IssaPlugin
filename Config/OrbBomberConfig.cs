using BepInEx.Configuration;
using UnityEngine.InputSystem;

namespace IssaPlugin
{
    public class OrbBomberConfig
    {
        private const string Section = "OrbBomber";

        public ConfigEntry<Key> GiveKey { get; private set; }
        public ConfigEntry<float> Uses { get; private set; }
        public ConfigEntry<float> MinSpawnRadius { get; private set; }
        public ConfigEntry<float> MaxSpawnRadius { get; private set; }
        public ConfigEntry<bool> ChaseGolfBall { get; private set; }
        public ConfigEntry<float> StartSpeed { get; private set; }
        public ConfigEntry<float> MaxSpeed { get; private set; }
        public ConfigEntry<float> FarSpeedDistance { get; private set; }
        public ConfigEntry<float> DetonationRange { get; private set; }
        public ConfigEntry<bool> CancelDetonationOutOfRange { get; private set; }
        public ConfigEntry<float> DetonationDuration { get; private set; }
        public ConfigEntry<float> FlashCount { get; private set; }
        public ConfigEntry<float> SizeMultiplier { get; private set; }
        public ConfigEntry<float> ExplosionScale { get; private set; }
        public ConfigEntry<float> ClubKnockbackForce { get; private set; }
        public ConfigEntry<float> ImpactExplodeChance { get; private set; }
        public ConfigEntry<float> SettleSpeed { get; private set; }
        public ConfigEntry<float> MaxLifetime { get; private set; }

        public OrbBomberConfig(ConfigFile cfg, GlobalConfig global)
        {
            GiveKey = cfg.Bind(
                Section,
                "GiveKey",
                Key.None,
                "Debug key to add the Orb Bomber to your inventory. Key.None disables it."
            );
            Uses = cfg.Bind(Section, "Uses", 1f, "Number of uses per Orb Bomber pickup.");
            MinSpawnRadius = cfg.Bind(
                Section,
                "MinSpawnRadius",
                32f,
                "Closest the orb may spawn to the victim, in metres."
            );
            MaxSpawnRadius = cfg.Bind(
                Section,
                "MaxSpawnRadius",
                128f,
                "Farthest the orb may spawn from the victim, in metres."
            );
            ChaseGolfBall = cfg.Bind(
                Section,
                "ChaseGolfBall",
                false,
                "If true, the orb chases the victim's golf ball. If false, it chases the victim."
            );
            StartSpeed = cfg.Bind(
                Section,
                "StartSpeed",
                3.5f,
                "Ground speed in metres per second at FarSpeedDistance or farther."
            );
            MaxSpeed = cfg.Bind(
                Section,
                "MaxSpeed",
                11f,
                "Ground speed in metres per second once the orb reaches DetonationRange."
            );
            FarSpeedDistance = cfg.Bind(
                Section,
                "FarSpeedDistance",
                16f,
                "Distance in metres at which the orb is still at StartSpeed. "
                    + "Inside this, speed rises to MaxSpeed at DetonationRange. "
                    + "Values at or below DetonationRange are raised so the blend stays valid."
            );
            DetonationRange = cfg.Bind(
                Section,
                "DetonationRange",
                4f,
                "Horizontal distance in metres at which the orb stops and starts the detonation sequence."
            );
            CancelDetonationOutOfRange = cfg.Bind(
                Section,
                "CancelDetonationOutOfRange",
                true,
                "If true, leaving DetonationRange during the countdown cancels it and the orb chases again. "
                    + "The victim has to knock it away with a swing to keep it from closing in. "
                    + "If false, the countdown commits once it starts."
            );
            DetonationDuration = cfg.Bind(
                Section,
                "DetonationDuration",
                2.5f,
                "Seconds the orb flashes and grows before it explodes."
            );
            FlashCount = cfg.Bind(
                Section,
                "FlashCount",
                4f,
                "How many white flashes play during DetonationDuration."
            );
            SizeMultiplier = cfg.Bind(
                Section,
                "SizeMultiplier",
                2f,
                "How many times larger the orb grows by the end of the detonation sequence. 1 keeps its spawn size."
            );
            ExplosionScale = cfg.Bind(
                Section,
                "ExplosionScale",
                1.5f,
                "Multiplier on the vanilla rocket explosion. Affects blast radius, knockback, and VFX size."
            );
            ClubKnockbackForce = cfg.Bind(
                Section,
                "ClubKnockbackForce",
                14f,
                "Impulse applied when the victim's swing hits the orb."
            );
            ImpactExplodeChance = cfg.Bind(
                Section,
                "ImpactExplodeChance",
                0.35f,
                new ConfigDescription(
                    "Chance, from 0 to 1, that a swing arms the orb to explode when it next really lands. "
                        + "A weak hit that never leaves the ground goes back to chasing.",
                    new AcceptableValueRange<float>(0f, 1f)
                )
            );
            SettleSpeed = cfg.Bind(
                Section,
                "SettleSpeed",
                1.25f,
                "Speed in metres per second below which a grounded orb, after a swing, starts chasing again."
            );
            MaxLifetime = cfg.Bind(
                Section,
                "MaxLifetime",
                120f,
                "Seconds after spawn before the orb is removed without exploding, including during detonation."
            );
        }
    }
}
