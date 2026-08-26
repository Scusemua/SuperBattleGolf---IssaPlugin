using BepInEx.Configuration;
using UnityEngine.InputSystem;

namespace IssaPlugin
{
    public class BearConfig
    {
        private const string Section = "Bear";

        public ConfigEntry<Key> GiveKey { get; private set; }
        public ConfigEntry<float> Uses { get; private set; }
        public ConfigEntry<float> Count { get; private set; }
        public ConfigEntry<float> SpawnRadius { get; private set; }
        public ConfigEntry<float> SessionDuration { get; private set; }
        public ConfigEntry<float> WalkSpeed { get; private set; }
        public ConfigEntry<float> RunSpeed { get; private set; }
        public ConfigEntry<float> ChargeSpeed { get; private set; }
        public ConfigEntry<float> TurnSpeed { get; private set; }
        public ConfigEntry<float> WanderRadius { get; private set; }
        public ConfigEntry<float> ChargeRange { get; private set; }
        public ConfigEntry<float> AttackRange { get; private set; }
        public ConfigEntry<float> AttackCooldown { get; private set; }
        public ConfigEntry<float> AttackAnimationDuration { get; private set; }
        public ConfigEntry<float> SpawnAnimationDuration { get; private set; }
        public ConfigEntry<float> DeathAnimationDuration { get; private set; }
        public ConfigEntry<float> StunDuration { get; private set; }
        public ConfigEntry<float> EnrageDuration { get; private set; }
        public ConfigEntry<float> EnrageSpeedMultiplier { get; private set; }
        public ConfigEntry<float> MaxHP { get; private set; }
        public ConfigEntry<float> DamageDuelingPistol { get; private set; }
        public ConfigEntry<float> DamageElephantGun { get; private set; }
        public ConfigEntry<float> DamageRocketDirect { get; private set; }
        public ConfigEntry<float> DamageRocketExplosion { get; private set; }
        public ConfigEntry<float> DamageGolfClub { get; private set; }
        public ConfigEntry<float> DamageBaseballBat { get; private set; }
        public ConfigEntry<float> DamageOrbitalLaser { get; private set; }
        public ConfigEntry<float> MeleeKnockbackForce { get; private set; }
        public ConfigEntry<float> BatKnockbackForce { get; private set; }
        public ConfigEntry<float> MaxClimbHeight { get; private set; }
        public ConfigEntry<float> TargetLockDuration { get; private set; }
        public ConfigEntry<float> TargetStealThreshold { get; private set; }
        public ConfigEntry<float> TargetAbandonDistance { get; private set; }
        public ConfigEntry<float> AggroStealThreshold { get; private set; }
        public ConfigEntry<float> AggroDuration { get; private set; }
        public ConfigEntry<float> MeleeHitRange { get; private set; }
        public ConfigEntry<bool> FriendlyFire { get; private set; }
        public ConfigEntry<bool> LogMovementDiagnostics { get; private set; }
        public ConfigEntry<bool> AttackFinishedPlayers { get; private set; }

        public BearConfig(ConfigFile cfg, GlobalConfig global)
        {
            GiveKey = cfg.Bind(
                Section,
                "GiveKey",
                Key.Numpad5,
                "Debug key to add the Bear item to your inventory."
            );
            Uses = cfg.Bind(Section, "Uses", 1f, "Number of uses per Bear item pickup.");
            Count = cfg.Bind(Section, "BearCount", 2f, "Number of bears spawned per item use.");
            SpawnRadius = cfg.Bind(
                Section,
                "SpawnRadius",
                15f,
                "Radius around the player within which bears spawn."
            );
            SessionDuration = cfg.Bind(
                Section,
                "SessionDuration",
                60f,
                "Seconds before all remaining bears are forcibly despawned."
            );
            WalkSpeed = cfg.Bind(
                Section,
                "WalkSpeed",
                4f,
                "Bear movement speed while wandering or obstructed (units/sec)."
            );
            RunSpeed = cfg.Bind(
                Section,
                "RunSpeed",
                9f,
                "Bear movement speed while pursuing a target (units/sec)."
            );
            ChargeSpeed = cfg.Bind(
                Section,
                "ChargeSpeed",
                14f,
                "Bear movement speed during the committed charge phase (units/sec)."
            );
            TurnSpeed = cfg.Bind(
                Section,
                "TurnSpeed",
                6f,
                "How quickly the bear rotates to face its movement direction."
            );
            WanderRadius = cfg.Bind(
                Section,
                "WanderRadius",
                12f,
                "Radius of the area the bear wanders within when idle."
            );
            ChargeRange = cfg.Bind(
                Section,
                "ChargeRange",
                10f,
                "Distance at which the bear commits to a charge attack (units)."
            );
            AttackRange = cfg.Bind(
                Section,
                "AttackRange",
                2.8f,
                "Distance at which the bear's attack swing connects (units)."
            );
            AttackCooldown = cfg.Bind(
                Section,
                "AttackCooldown",
                0.7f,
                "Seconds of recovery after an attack before the bear can swipe again. "
                    + "The bear keeps tracking its target during this window rather than "
                    + "standing still."
            );
            AttackAnimationDuration = cfg.Bind(
                Section,
                "AttackAnimationDuration",
                1.2f,
                "Total duration of the attack animation. Hit is applied at 55% of this value."
            );
            SpawnAnimationDuration = cfg.Bind(
                Section,
                "SpawnAnimationDuration",
                2.0f,
                "Duration of the Buff/spawn-in animation before the bear begins hunting."
            );
            DeathAnimationDuration = cfg.Bind(
                Section,
                "DeathAnimationDuration",
                2.5f,
                "How long the death animation plays before the bear is destroyed."
            );
            StunDuration = cfg.Bind(
                Section,
                "StunDuration",
                2.0f,
                "Seconds the bear is stunned after being hit by an explosion."
            );
            EnrageDuration = cfg.Bind(
                Section,
                "EnrageDuration",
                5f,
                "Seconds the bear runs at enrage speed after recovering from a stun."
            );
            EnrageSpeedMultiplier = cfg.Bind(
                Section,
                "EnrageSpeedMultiplier",
                1.5f,
                "Speed multiplier applied to RunSpeed during enrage."
            );
            MaxHP = cfg.Bind(
                Section,
                "MaxHP",
                100f,
                "Maximum HP for a bear. Each hit reduces HP by the weapon's damage value; bear dies at 0."
            );
            DamageDuelingPistol = cfg.Bind(
                Section,
                "DamageDuelingPistol",
                25f,
                "HP damage dealt to a bear by a dueling pistol shot."
            );
            DamageElephantGun = cfg.Bind(
                Section,
                "DamageElephantGun",
                50f,
                "HP damage dealt to a bear by an elephant gun shot."
            );
            DamageRocketDirect = cfg.Bind(
                Section,
                "DamageRocketDirect",
                100f,
                "HP damage dealt to a bear by a direct rocket hit (explosion centre within 1.5 units)."
            );
            DamageRocketExplosion = cfg.Bind(
                Section,
                "DamageRocketExplosion",
                35f,
                "HP damage dealt to a bear by rocket splash damage (not a direct hit)."
            );
            DamageGolfClub = cfg.Bind(
                Section,
                "DamageGolfClub",
                25f,
                "HP damage dealt to a bear by a golf club swing."
            );
            DamageBaseballBat = cfg.Bind(
                Section,
                "DamageBaseballBat",
                40f,
                "HP damage dealt to a bear by a baseball bat swing."
            );
            DamageOrbitalLaser = cfg.Bind(
                Section,
                "DamageOrbitalLaser",
                100f,
                "HP damage dealt to a bear by the orbital laser."
            );
            MeleeKnockbackForce = cfg.Bind(
                Section,
                "MeleeKnockbackForce",
                12f,
                "Impulse force applied to a bear's rigidbody when hit by a golf club swing."
            );
            BatKnockbackForce = cfg.Bind(
                Section,
                "BatKnockbackForce",
                22f,
                "Impulse force applied to a bear's rigidbody when hit by a baseball bat swing."
            );
            MaxClimbHeight = cfg.Bind(
                Section,
                "MaxClimbHeight",
                6f,
                "Max height difference (units) before a target is considered unreachable."
            );
            TargetLockDuration = cfg.Bind(
                Section,
                "TargetLockDuration",
                8f,
                "Minimum seconds a bear keeps its target before re-evaluating."
            );
            TargetStealThreshold = cfg.Bind(
                Section,
                "TargetStealThreshold",
                12f,
                "A new candidate must be this many units closer to steal the lock after the timer expires."
            );
            TargetAbandonDistance = cfg.Bind(
                Section,
                "TargetAbandonDistance",
                65f,
                "If the locked target moves beyond this distance the lock is dropped immediately."
            );
            AggroStealThreshold = cfg.Bind(
                Section,
                "AggroStealThreshold",
                4f,
                "A player who hit the bear can steal the lock if at least this many units closer (lower bar than normal steal)."
            );
            AggroDuration = cfg.Bind(
                Section,
                "AggroDuration",
                15f,
                "How long (seconds) aggro on a specific player lasts after they hit the bear."
            );
            MeleeHitRange = cfg.Bind(
                Section,
                "MeleeHitRange",
                2.5f,
                "Radius (units) around the swing hitbox centre that counts as a golf-club/bat hit on a bear."
            );
            FriendlyFire = cfg.Bind(
                Section,
                "FriendlyFire",
                false,
                "If false, bears will not target or attack the player who summoned them."
            );
            LogMovementDiagnostics = cfg.Bind(
                Section,
                "LogMovementDiagnostics",
                false,
                "Logs once per second how far each bear intended to move versus how far it "
                    + "actually moved. Use to diagnose bears that animate but stay in place."
            );
            AttackFinishedPlayers = cfg.Bind(
                Section,
                "AttackFinishedPlayers",
                false,
                "If true, bears will target players who have already holed out. If false, bears ignore them."
            );
        }
    }
}
