using BepInEx.Configuration;
using UnityEngine.InputSystem;

namespace IssaPlugin
{
    public static class Configuration
    {
        // Master kill-switch for allowing custom items to be spawned without
        // having to set all spawn weights to 0.
        public static ConfigEntry<bool> CustomItemSpawnsEnabled { get; private set; }

        // --- Baseball Bat ---
        public static ConfigEntry<float> BaseballBatPowerMultiplier { get; private set; }
        public static ConfigEntry<float> BaseballBatUses { get; private set; }
        public static ConfigEntry<Key> BaseballBatGiveKey { get; private set; }
        public static ConfigEntry<float> BaseballBatSpawnWeight { get; private set; }

        // --- Stealth Bomber ---
        public static ConfigEntry<float> BomberAltitude { get; private set; }
        public static ConfigEntry<float> BomberSpeed { get; private set; }
        public static ConfigEntry<float> BomberRocketInterval { get; private set; }
        public static ConfigEntry<float> BomberSpread { get; private set; }
        public static ConfigEntry<float> BomberUses { get; private set; }
        public static ConfigEntry<Key> BomberGiveKey { get; private set; }
        public static ConfigEntry<float> BomberWaitTime { get; private set; }
        public static ConfigEntry<float> BomberStripLength { get; private set; }
        public static ConfigEntry<float> BomberRocketAngularJitter { get; private set; }
        public static ConfigEntry<float> BomberTargetingZoomSpeed { get; private set; }
        public static ConfigEntry<float> BomberTargetMoveSpeed { get; private set; }
        public static ConfigEntry<float> BomberTargetRotateSpeed { get; private set; }
        public static ConfigEntry<float> BomberSpawnWeight { get; private set; }
        public static ConfigEntry<float> BomberApproachDistance { get; private set; }
        public static ConfigEntry<float> BomberHitsToDestroy { get; private set; }
        public static ConfigEntry<float> BomberCrashImpactForce { get; private set; }
        public static ConfigEntry<float> BomberCrashDownwardForce { get; private set; }

        public static ConfigEntry<float> BomberCrashTorque { get; private set; }

        // --- Predator Missile ---
        public static ConfigEntry<float> MissileAltitude { get; private set; }
        public static ConfigEntry<float> MissileFallSpeed { get; private set; }
        public static ConfigEntry<float> MissileSteerSpeed { get; private set; }
        public static ConfigEntry<float> MissileTimeout { get; private set; }
        public static ConfigEntry<float> MissileUses { get; private set; }
        public static ConfigEntry<Key> MissileGiveKey { get; private set; }
        public static ConfigEntry<float> MissileSpawnWeight { get; private set; }

        // --- AC130 ---
        public static ConfigEntry<float> AC130Uses { get; private set; }
        public static ConfigEntry<Key> AC130GiveKey { get; private set; }
        public static ConfigEntry<float> AC130SpawnWeight { get; private set; }
        public static ConfigEntry<float> AC130OrbitRadius { get; private set; }
        public static ConfigEntry<float> AC130OrbitSpeed { get; private set; }
        public static ConfigEntry<float> AC130Altitude { get; private set; }
        public static ConfigEntry<float> AC130Duration { get; private set; }
        public static ConfigEntry<float> AC130CameraPitch { get; private set; }
        public static ConfigEntry<float> AC130CameraDistance { get; private set; }
        public static ConfigEntry<float> AC130FireCooldown { get; private set; }
        public static ConfigEntry<float> AC130RocketAngularJitter { get; private set; }
        public static ConfigEntry<float> AC130BoostMultiplier { get; private set; }
        public static ConfigEntry<float> AC130AltitudeOffsetMax { get; private set; }
        public static ConfigEntry<float> AC130AltitudeAdjustSpeed { get; private set; }
        public static ConfigEntry<float> AC130ZoomFov { get; private set; }
        public static ConfigEntry<float> AC130ZoomSpeed { get; private set; }
        public static ConfigEntry<float> AC130ApproachDistance { get; private set; }
        public static ConfigEntry<float> AC130ApproachSpeed { get; private set; }
        public static ConfigEntry<float> AC130BaseFov { get; private set; }

        public static ConfigEntry<float> AC130YawLimit { get; private set; }
        public static ConfigEntry<float> AC130PitchLimit { get; private set; }
        public static ConfigEntry<float> AC130MouseSensitivity { get; private set; }

        // --- AC130 Mayday ---
        public static ConfigEntry<bool> AC130MaydayEnabled { get; private set; }
        public static ConfigEntry<Key> AC130MaydayKey { get; private set; }
        public static ConfigEntry<float> AC130MaydayDiveSteepRate { get; private set; }
        public static ConfigEntry<float> AC130MaydayInitialDiveAngle { get; private set; }
        public static ConfigEntry<float> AC130MaydayMaxDiveAngle { get; private set; }
        public static ConfigEntry<float> AC130MaydayPullInfluence { get; private set; }
        public static ConfigEntry<float> AC130MaydayRollSpeed { get; private set; }
        public static ConfigEntry<float> AC130MaydaySpeed { get; private set; }
        public static ConfigEntry<float> AC130MaydayDrift { get; private set; }
        public static ConfigEntry<float> AC130MaydayCenterBias { get; private set; }
        public static ConfigEntry<float> AC130MaydayCamYawLimit { get; private set; }
        public static ConfigEntry<float> AC130MaydayCamPitchLimit { get; private set; }
        public static ConfigEntry<float> AC130MaydayShakeBase { get; private set; }
        public static ConfigEntry<float> AC130MaydayShakeMax { get; private set; }
        public static ConfigEntry<float> AC130MaydayExplosionScale { get; private set; }
        public static ConfigEntry<float> AC130MaydayExplosionDuration { get; private set; }
        public static ConfigEntry<float> AC130MaydayRollTurnRate { get; private set; }
        public static ConfigEntry<float> AC130HitsToMayday { get; private set; }
        public static ConfigEntry<float> AC130RocketProximityFuse { get; private set; }

        // --- Freeze World ---
        public static ConfigEntry<Key> FreezeGiveKey { get; private set; }
        public static ConfigEntry<float> FreezeUses { get; private set; }
        public static ConfigEntry<float> FreezeDuration { get; private set; }
        public static ConfigEntry<float> FreezeFriction { get; private set; }
        public static ConfigEntry<float> FreezeBounciness { get; private set; }
        public static ConfigEntry<float> FreezeCartSidewaysStiffness { get; private set; }
        public static ConfigEntry<float> FreezeGripRadius { get; private set; }
        public static ConfigEntry<float> FreezeSpawnWeight { get; private set; }

        // --- Low Gravity ---
        public static ConfigEntry<Key> LowGravityGiveKey { get; private set; }
        public static ConfigEntry<float> LowGravityUses { get; private set; }
        public static ConfigEntry<float> LowGravityDuration { get; private set; }
        public static ConfigEntry<float> LowGravityScale { get; private set; }
        public static ConfigEntry<float> LowGravitySpawnWeight { get; private set; }

        // --- Sniper Rifle ---
        public static ConfigEntry<Key> SniperRifleGiveKey { get; private set; }
        public static ConfigEntry<float> SniperRifleUses { get; private set; }
        public static ConfigEntry<float> SniperRifleSpawnWeight { get; private set; }
        public static ConfigEntry<float> SniperRifleMaxAimingDistance { get; private set; }
        public static ConfigEntry<float> SniperRifleMaxShotDistance { get; private set; }
        public static ConfigEntry<float> SniperRifleScopedInaccuracy { get; private set; }
        public static ConfigEntry<float> SniperRifleHipFireInaccuracy { get; private set; }
        public static ConfigEntry<float> SniperRifleZoomFov { get; private set; }
        public static ConfigEntry<float> SniperRifleZoomSpeed { get; private set; }
        public static ConfigEntry<float> SniperRifleShotDuration { get; private set; }
        public static ConfigEntry<float> SniperRifleMinZoomFov { get; private set; }
        public static ConfigEntry<float> SniperRifleMaxZoomFov { get; private set; }
        public static ConfigEntry<float> SniperRifleScrollSensitivity { get; private set; }

        // --- Donut ---
        public static ConfigEntry<Key> DonutGiveKey { get; private set; }
        public static ConfigEntry<float> DonutUses { get; private set; }
        public static ConfigEntry<float> DonutSpawnWeight { get; private set; }
        public static ConfigEntry<float> DonutSpeed { get; private set; }
        public static ConfigEntry<float> DonutAltitude { get; private set; }
        public static ConfigEntry<float> DonutTerrainFollowSpeed { get; private set; }
        public static ConfigEntry<float> DonutTurnSpeed { get; private set; }
        public static ConfigEntry<float> DonutCameraPitch { get; private set; }
        public static ConfigEntry<float> DonutCameraDistance { get; private set; }
        public static ConfigEntry<float> DonutMouseSensitivity { get; private set; }
        public static ConfigEntry<float> DonutDuration { get; private set; }
        public static ConfigEntry<float> DonutLaserUses { get; private set; }
        public static ConfigEntry<float> DonutLaserAnticipationDuration { get; private set; }
        public static ConfigEntry<float> DonutLaserCooldown { get; private set; }
        public static ConfigEntry<float> DonutHitsToDestroy { get; private set; }
        public static ConfigEntry<float> DonutCrashImpactForce { get; private set; }
        public static ConfigEntry<float> DonutCrashDownwardForce { get; private set; }
        public static ConfigEntry<float> DonutCrashTorque { get; private set; }
        public static ConfigEntry<float> DonutCrashExplosionScale { get; private set; }

        // --- StickyGrenade ---
        public static ConfigEntry<Key> StickyGrenadeGiveKey { get; private set; }
        public static ConfigEntry<float> StickyGrenadeUses { get; private set; }
        public static ConfigEntry<float> StickyGrenadeSpawnWeight { get; private set; }
        public static ConfigEntry<float> StickyGrenadeThrowSpeed { get; private set; }
        public static ConfigEntry<float> StickyGrenadeMaxThrowSpeed { get; private set; }
        public static ConfigEntry<float> StickyGrenadeLobAngle { get; private set; }
        public static ConfigEntry<float> StickyGrenadeFuseTime { get; private set; }
        public static ConfigEntry<float> StickyGrenadeGraceTime { get; private set; }
        public static ConfigEntry<float> StickyGrenadeStickRadius { get; private set; }
        public static ConfigEntry<float> StickyGrenadeExplosionScale { get; private set; }

        // --- Javelin ---
        public static ConfigEntry<Key> JavelinGiveKey { get; private set; }
        public static ConfigEntry<float> JavelinUses { get; private set; }
        public static ConfigEntry<float> JavelinSpawnWeight { get; private set; }
        public static ConfigEntry<float> JavelinApexHeight { get; private set; }
        public static ConfigEntry<float> JavelinAscentSpeed { get; private set; }
        public static ConfigEntry<float> JavelinDiveSpeed { get; private set; }
        public static ConfigEntry<float> JavelinDiveAcceleration { get; private set; }
        public static ConfigEntry<float> JavelinArrivalRadius { get; private set; }
        public static ConfigEntry<float> JavelinTimeout { get; private set; }
        public static ConfigEntry<float> JavelinExplosionVfxDuration { get; private set; }

        // --- Explosion Scaling ---
        public static ConfigEntry<float> AC130ExplosionScale { get; private set; }
        public static ConfigEntry<float> PredatorMissileExplosionScale { get; private set; }
        public static ConfigEntry<float> StealthBomberExplosionScale { get; private set; }
        public static ConfigEntry<float> JavelinExplosionScale { get; private set; }

        // --- Nuke ---
        public static ConfigEntry<Key> NukeGiveKey { get; private set; }
        public static ConfigEntry<float> NukeUses { get; private set; }
        public static ConfigEntry<float> NukeSpawnWeight { get; private set; }
        public static ConfigEntry<float> NukeDropHeight { get; private set; }
        public static ConfigEntry<float> NukeDropSpeed { get; private set; }
        public static ConfigEntry<float> NukeExplosionScale { get; private set; }
        public static ConfigEntry<float> NukeSkyBlastForce { get; private set; }
        public static ConfigEntry<float> NukeSkyBlastRadius { get; private set; }
        public static ConfigEntry<float> NukeSkyBlastVerticalBias { get; private set; }
        public static ConfigEntry<float> NukeExplosionVfxDuration { get; private set; }
        public static ConfigEntry<float> NukeExplosionVfxScale { get; private set; }
        public static ConfigEntry<bool> NukeExcludeThrower { get; private set; }

        // --- Black Hole Grenade ---
        public static ConfigEntry<Key> BlackHoleGrenadeGiveKey { get; private set; }
        public static ConfigEntry<float> BlackHoleGrenadeUses { get; private set; }
        public static ConfigEntry<float> BlackHoleGrenadeSpawnWeight { get; private set; }
        public static ConfigEntry<float> BlackHoleGrenadeThrowSpeed { get; private set; }
        public static ConfigEntry<float> BlackHoleGrenadeMaxThrowSpeed { get; private set; }
        public static ConfigEntry<float> BlackHoleGrenadeLobAngle { get; private set; }
        public static ConfigEntry<float> BlackHoleGrenadeGraceTime { get; private set; }
        public static ConfigEntry<float> BlackHoleGrenadeSuckDuration { get; private set; }
        public static ConfigEntry<float> BlackHoleGrenadeSuckRadius { get; private set; }
        public static ConfigEntry<float> BlackHoleGrenadeSuckForce { get; private set; }
        public static ConfigEntry<float> BlackHoleGrenadeMaxSuckForce { get; private set; }
        public static ConfigEntry<float> BlackHoleGrenadeSpitForce { get; private set; }
        public static ConfigEntry<float> BlackHoleGrenadeSpitVfxScale { get; private set; }
        public static ConfigEntry<float> BlackHoleGrenadeKnockdownRadius { get; private set; }
        public static ConfigEntry<bool> BlackHoleGrenadeExcludeThrower { get; private set; }
        public static ConfigEntry<float> BlackHoleGrenadeBonusGolfCartForceMultiplier
        {
            get;
            private set;
        }
        public static ConfigEntry<bool> BlackHoleGrenadeAffectsGolfBalls { get; private set; }

        // --- Placeable Wall ---
        public static ConfigEntry<Key> PlaceableWallGiveKey { get; private set; }
        public static ConfigEntry<float> PlaceableWallUses { get; private set; }
        public static ConfigEntry<float> PlaceableWallSpawnWeight { get; private set; }
        public static ConfigEntry<float> PlaceableWallMaxPlacementDistance { get; private set; }
        public static ConfigEntry<float> PlaceableWallMinHoleDistance { get; private set; }
        public static ConfigEntry<float> PlaceableWallHealthPoints { get; private set; }

        public static ConfigEntry<float> PlaceableWallVelocityImpactFactor { get; private set; }

        public static ConfigEntry<float> PlaceableWallTorsionMultiplier { get; private set; }
        public static ConfigEntry<float> PlaceableWallRotationStep { get; private set; }
        public static ConfigEntry<float> PlaceableWallDebrisLifetime { get; private set; }
        public static ConfigEntry<float> PlaceableWallRocketExplosionForce { get; private set; }
        public static ConfigEntry<float> PlaceableWallDamageGolfClub { get; private set; }
        public static ConfigEntry<float> PlaceableWallDamageBaseballBat { get; private set; }

        // --- Sub-Machine Gun ---
        public static ConfigEntry<Key> SubMachineGunGiveKey { get; private set; }
        public static ConfigEntry<float> SubMachineGunUses { get; private set; }
        public static ConfigEntry<float> SubMachineGunSpawnWeight { get; private set; }
        public static ConfigEntry<float> SubMachineGunFireRate { get; private set; }
        public static ConfigEntry<float> SubMachineGunInaccuracy { get; private set; }
        public static ConfigEntry<float> SubMachineGunMaxAimingDistance { get; private set; }
        public static ConfigEntry<float> SubMachineGunMaxShotDistance { get; private set; }

        // --- Bear ---
        public static ConfigEntry<Key> BearGiveKey { get; private set; }
        public static ConfigEntry<float> BearUses { get; private set; }
        public static ConfigEntry<float> BearSpawnWeight { get; private set; }
        public static ConfigEntry<float> BearCount { get; private set; }
        public static ConfigEntry<float> BearSpawnRadius { get; private set; }
        public static ConfigEntry<float> BearSessionDuration { get; private set; }
        public static ConfigEntry<float> BearWalkSpeed { get; private set; }
        public static ConfigEntry<float> BearRunSpeed { get; private set; }
        public static ConfigEntry<float> BearChargeSpeed { get; private set; }
        public static ConfigEntry<float> BearTurnSpeed { get; private set; }
        public static ConfigEntry<float> BearWanderRadius { get; private set; }
        public static ConfigEntry<float> BearChargeRange { get; private set; }
        public static ConfigEntry<float> BearAttackRange { get; private set; }
        public static ConfigEntry<float> BearAttackCooldown { get; private set; }
        public static ConfigEntry<float> BearAttackAnimationDuration { get; private set; }
        public static ConfigEntry<float> BearSpawnAnimationDuration { get; private set; }
        public static ConfigEntry<float> BearDeathAnimationDuration { get; private set; }
        public static ConfigEntry<float> BearStunDuration { get; private set; }
        public static ConfigEntry<float> BearEnrageDuration { get; private set; }
        public static ConfigEntry<float> BearEnrageSpeedMultiplier { get; private set; }
        public static ConfigEntry<float> BearMaxHP { get; private set; }
        public static ConfigEntry<float> BearDamageDuelingPistol { get; private set; }
        public static ConfigEntry<float> BearDamageElephantGun { get; private set; }
        public static ConfigEntry<float> BearDamageRocketDirect { get; private set; }
        public static ConfigEntry<float> BearDamageRocketExplosion { get; private set; }
        public static ConfigEntry<float> BearDamageGolfClub { get; private set; }
        public static ConfigEntry<float> BearDamageBaseballBat { get; private set; }
        public static ConfigEntry<float> BearDamageOrbitalLaser { get; private set; }
        public static ConfigEntry<float> BearMeleeKnockbackForce { get; private set; }
        public static ConfigEntry<float> BearBatKnockbackForce { get; private set; }
        public static ConfigEntry<float> BearMaxClimbHeight { get; private set; }
        public static ConfigEntry<float> BearTargetLockDuration { get; private set; }
        public static ConfigEntry<float> BearTargetStealThreshold { get; private set; }
        public static ConfigEntry<float> BearTargetAbandonDistance { get; private set; }
        public static ConfigEntry<float> BearAggroStealThreshold { get; private set; }
        public static ConfigEntry<float> BearAggroDuration { get; private set; }
        public static ConfigEntry<float> BearMeleeHitRange { get; private set; }
        public static ConfigEntry<bool> BearFriendlyFire { get; private set; }

        public static void Initialize(ConfigFile cfg)
        {
            CustomItemSpawnsEnabled = cfg.Bind(
                "IssaPlugin",
                "Enabled",
                true,
                "Master kill-switch for allowing custom items to be spawned without having to set all spawn weights to 0."
            );

            // --- Baseball Bat ---
            BaseballBatPowerMultiplier = cfg.Bind(
                "BaseballBat",
                "PowerMultiplier",
                3.0f,
                "Multiplier applied to the golf swing power when using the bat."
            );

            BaseballBatUses = cfg.Bind(
                "BaseballBat",
                "Uses",
                99.0f,
                "Number of swings before the bat is consumed. Set high for near-infinite use."
            );

            BaseballBatGiveKey = cfg.Bind(
                "BaseballBat",
                "GiveKey",
                Key.F7,
                "Key to press to add the baseball bat to your inventory."
            );

            // --- Stealth Bomber ---
            BomberAltitude = cfg.Bind(
                "StealthBomber",
                "Altitude",
                50f,
                "Height above the map the bombing run flies at."
            );

            BomberSpeed = cfg.Bind(
                "StealthBomber",
                "Speed",
                40f,
                "Speed of the bombing run in units per second."
            );

            BomberApproachDistance = cfg.Bind(
                "StealthBomber",
                "ApproachDistance",
                300f,
                "How far away the bomber visual spawns from the targeting strip in units."
            );

            BomberRocketInterval = cfg.Bind(
                "StealthBomber",
                "RocketInterval",
                0.15f,
                "Seconds between each rocket drop during a bombing run."
            );

            BomberSpread = cfg.Bind(
                "StealthBomber",
                "Spread",
                5f,
                "Random lateral spread in units for each rocket's drop position."
            );

            BomberUses = cfg.Bind(
                "StealthBomber",
                "Uses",
                1f,
                "Number of bombing runs per pickup."
            );

            BomberGiveKey = cfg.Bind(
                "StealthBomber",
                "GiveKey",
                Key.F8,
                "Key to press to add the stealth bomber to your inventory."
            );

            BomberWaitTime = cfg.Bind(
                "StealthBomber",
                "WaitTime",
                1.5f,
                "Seconds to wait before starting the bombing run."
            );

            BomberStripLength = cfg.Bind(
                "StealthBomber",
                "StripLength",
                300f,
                "Length of the targeting strip in units."
            );

            BomberTargetingZoomSpeed = cfg.Bind(
                "StealthBomber",
                "TargetingZoomSpeed",
                0.05f,
                "Speed at which the camera zooms in/out during bomber targeting."
            );

            BomberRocketAngularJitter = cfg.Bind(
                "StealthBomber",
                "RocketAngularJitter",
                0.8f,
                "Random angular jitter in degrees for each rocket's rotation."
            );

            BomberTargetMoveSpeed = cfg.Bind(
                "StealthBomber",
                "TargetMoveSpeed",
                50f,
                "How fast the targeting strip moves with WASD."
            );

            BomberTargetRotateSpeed = cfg.Bind(
                "StealthBomber",
                "TargetRotateSpeed",
                90f,
                "Rotation speed of the targeting strip in degrees per second (Q/E)."
            );

            BomberHitsToDestroy = cfg.Bind(
                "StealthBomber",
                "HitsToDestroy",
                1f,
                "Rocket hits required to shoot down the bomber and cancel its run. Set to 0 to make it invincible."
            );

            BomberCrashImpactForce = cfg.Bind(
                "StealthBomber",
                "CrashImpactForce",
                500f,
                "Impulse force applied to the stealth bomber in the direction of the rocket hit when shot down."
            );

            BomberCrashDownwardForce = cfg.Bind(
                "StealthBomber",
                "CrashDownwardForce",
                15f,
                "Impulse force applied to the stealth bomber in the downward direction."
            );

            BomberCrashTorque = cfg.Bind(
                "StealthBomber",
                "CrashTorque",
                1.2f,
                "Magnitude of the random tumble torque applied to the stealth bomber when shot down."
            );

            // --- Predator Missile ---
            MissileAltitude = cfg.Bind(
                "PredatorMissile",
                "Altitude",
                175f,
                "Height above the player where the missile spawns."
            );

            MissileFallSpeed = cfg.Bind(
                "PredatorMissile",
                "FallSpeed",
                30f,
                "Downward speed of the missile in units per second."
            );

            MissileSteerSpeed = cfg.Bind(
                "PredatorMissile",
                "SteerSpeed",
                25f,
                "Horizontal steering speed when directing the missile."
            );

            MissileTimeout = cfg.Bind(
                "PredatorMissile",
                "Timeout",
                15f,
                "Maximum seconds before the missile auto-detonates."
            );

            MissileUses = cfg.Bind(
                "PredatorMissile",
                "Uses",
                1f,
                "Number of missile uses per pickup."
            );

            MissileGiveKey = cfg.Bind(
                "PredatorMissile",
                "GiveKey",
                Key.F9,
                "Key to press to add the predator missile to your inventory."
            );

            // --- Item Box Spawn Weights ---
            BaseballBatSpawnWeight = cfg.Bind(
                "ItemBoxSpawns",
                "BaseballBatWeight",
                5f,
                "Spawn weight for the baseball bat in item boxes. Set to 0 to disable."
            );

            BomberSpawnWeight = cfg.Bind(
                "ItemBoxSpawns",
                "StealthBomberWeight",
                2f,
                "Spawn weight for the stealth bomber in item boxes. Set to 0 to disable."
            );

            MissileSpawnWeight = cfg.Bind(
                "ItemBoxSpawns",
                "PredatorMissileWeight",
                3f,
                "Spawn weight for the predator missile in item boxes. Set to 0 to disable."
            );

            AC130SpawnWeight = cfg.Bind(
                "ItemBoxSpawns",
                "AC130Weight",
                1f,
                "Spawn weight for the AC130 in item boxes. Set to 0 to disable."
            );

            AC130Uses = cfg.Bind("AC130", "Uses", 1f, "Number of AC130 uses per pickup.");

            AC130GiveKey = cfg.Bind(
                "AC130",
                "GiveKey",
                Key.F11,
                "Key to press to add the AC130 to your inventory."
            );

            AC130OrbitRadius = cfg.Bind(
                "AC130",
                "OrbitRadius",
                400f,
                "Radius in units of the circle the gunship flies around the map centre."
            );

            AC130OrbitSpeed = cfg.Bind(
                "AC130",
                "OrbitSpeed",
                12f,
                "Degrees per second at which the gunship orbits the map centre."
            );

            AC130Altitude = cfg.Bind(
                "AC130",
                "Altitude",
                120f,
                "Height above the map centre the gunship flies at."
            );

            AC130Duration = cfg.Bind(
                "AC130",
                "Duration",
                40f,
                "How many seconds the AC130 remains active before leaving."
            );

            AC130CameraPitch = cfg.Bind(
                "AC130",
                "CameraPitch",
                80f,
                "Camera pitch angle in degrees during the AC130 (0 = horizontal, 90 = straight down)."
            );

            AC130CameraDistance = cfg.Bind(
                "AC130",
                "CameraDistance",
                80f,
                "Camera distance addition from the gunship pivot during the AC130."
            );

            AC130FireCooldown = cfg.Bind(
                "AC130",
                "FireCooldown",
                0.8f,
                "Minimum seconds between rocket fires."
            );

            AC130RocketAngularJitter = cfg.Bind(
                "AC130",
                "RocketAngularJitter",
                0.5f,
                "Random angular jitter in degrees applied to each rocket fired from the AC130."
            );

            AC130BoostMultiplier = cfg.Bind(
                "AC130",
                "BoostMultiplier",
                1.25f,
                "Multiplier applied to orbit speed when holding Left Shift."
            );

            AC130AltitudeOffsetMax = cfg.Bind(
                "AC130",
                "AltitudeOffsetMax",
                80f,
                "Maximum units the player can raise the gunship from its base altitude using Q/E."
            );

            AC130AltitudeAdjustSpeed = cfg.Bind(
                "AC130",
                "AltitudeAdjustSpeed",
                10f,
                "Units per second the gunship rises or descends when holding Q or E."
            );

            AC130ZoomFov = cfg.Bind(
                "AC130",
                "ZoomFov",
                20f,
                "Field of view when right-click zooming in the AC130. Lower values zoom in more (default camera FOV is typically 60)."
            );

            AC130ZoomSpeed = cfg.Bind(
                "AC130",
                "ZoomSpeed",
                8f,
                "Speed at which the camera lerps to and from the zoomed FOV when right-clicking."
            );

            AC130ApproachDistance = cfg.Bind(
                "AC130",
                "ApproachDistance",
                800f,
                "How far away the AC130 spawns and flies in from before reaching the orbit point."
            );

            AC130ApproachSpeed = cfg.Bind(
                "AC130",
                "ApproachSpeed",
                120f,
                "Speed in units per second at which the AC130 flies in and out."
            );

            // --- Freeze World ---
            FreezeGiveKey = cfg.Bind(
                "FreezeWorld",
                "GiveKey",
                Key.F12,
                "Debug key to add the Freeze World item to your inventory."
            );

            FreezeUses = cfg.Bind(
                "FreezeWorld",
                "Uses",
                1f,
                "Number of uses per Freeze World pickup."
            );

            FreezeDuration = cfg.Bind(
                "FreezeWorld",
                "Duration",
                15f,
                "Seconds the world stays frozen before physics and visuals are restored."
            );

            FreezeFriction = cfg.Bind(
                "FreezeWorld",
                "Friction",
                0.02f,
                "Surface friction applied to all physics contacts during a freeze (0 = frictionless)."
            );

            FreezeBounciness = cfg.Bind(
                "FreezeWorld",
                "Bounciness",
                0.2f,
                new ConfigDescription(
                    "Surface bounciness applied to all physics contacts during a freeze.",
                    new AcceptableValueRange<float>(0.0f, 1.0f)
                )
            );

            FreezeCartSidewaysStiffness = cfg.Bind(
                "FreezeWorld",
                "CartSidewaysStiffness",
                0.15f,
                "Sideways friction stiffness for golf cart wheel colliders while frozen (0 = no grip, 1 = normal). Lower values cause more drift."
            );

            FreezeGripRadius = cfg.Bind(
                "FreezeWorld",
                "GripRadius",
                1.5f,
                "Distance (metres) from the local player's own ball within which normal traction is restored, allowing them to stop and take a shot."
            );

            FreezeSpawnWeight = cfg.Bind(
                "ItemBoxSpawns",
                "FreezeWorldWeight",
                2f,
                "Spawn weight for the Freeze World item in item boxes. Set to 0 to disable."
            );

            // --- Explosion Scaling ---
            AC130ExplosionScale = cfg.Bind(
                "Explosions",
                "AC130Scale",
                2.25f,
                "Multiplier for AC130 rocket explosions. Affects blast radius, knockback, and VFX size."
            );

            PredatorMissileExplosionScale = cfg.Bind(
                "Explosions",
                "PredatorMissileScale",
                3.0f,
                "Multiplier for Predator Missile explosions. Affects blast radius, knockback, and VFX size."
            );

            StealthBomberExplosionScale = cfg.Bind(
                "Explosions",
                "StealthBomberScale",
                1.5f,
                "Multiplier for Stealth Bomber rocket explosions. Affects blast radius, knockback, and VFX size."
            );

            AC130BaseFov = cfg.Bind(
                "AC130",
                "BaseFov",
                60f,
                "Base field of view for the AC130 camera."
            );

            AC130YawLimit = cfg.Bind(
                "AC130",
                "YawLimit",
                40f,
                "How many degrees left/right the player can pan from the map centre."
            );

            AC130PitchLimit = cfg.Bind(
                "AC130",
                "PitchLimit",
                30f,
                "How many degrees up/down the player can pan from the map centre."
            );

            AC130MouseSensitivity = cfg.Bind(
                "AC130",
                "MouseSensitivity",
                0.15f,
                "How sensitive the player's mouse is to panning the camera."
            );

            // --- AC130 Mayday ---
            AC130MaydayEnabled = cfg.Bind(
                "AC130Mayday",
                "Enabled",
                true,
                "Whether the manual mayday self-destruct hotkey is available."
            );

            AC130MaydayKey = cfg.Bind(
                "AC130Mayday",
                "Key",
                Key.M,
                "Hotkey to manually trigger mayday (self-destruct) while in an AC130 session."
            );

            AC130MaydayDiveSteepRate = cfg.Bind(
                "AC130Mayday",
                "DiveSteepRate",
                8f,
                "Degrees per second at which the dive pitch steepens toward vertical."
            );

            AC130MaydayInitialDiveAngle = cfg.Bind(
                "AC130Mayday",
                "InitialDiveAngle",
                20f,
                "Starting pitch angle (degrees below horizontal) when mayday begins."
            );

            AC130MaydayMaxDiveAngle = cfg.Bind(
                "AC130Mayday",
                "MaxDiveAngle",
                85f,
                "Maximum pitch angle (degrees below horizontal) the dive steepens to."
            );

            AC130MaydayPullInfluence = cfg.Bind(
                "AC130Mayday",
                "PullInfluence",
                6f,
                "Degrees per second of pitch influence the player has when holding W/S during mayday."
            );

            AC130MaydayRollSpeed = cfg.Bind(
                "AC130Mayday",
                "RollSpeed",
                45f,
                "Degrees per second the player can roll the gunship with A/D during mayday."
            );

            AC130MaydaySpeed = cfg.Bind(
                "AC130Mayday",
                "Speed",
                80f,
                "Forward speed of the gunship during the mayday dive in units per second."
            );

            AC130MaydayDrift = cfg.Bind(
                "AC130Mayday",
                "Drift",
                3f,
                "Maximum random lateral drift added to the dive direction per second."
            );

            AC130MaydayCenterBias = cfg.Bind(
                "AC130Mayday",
                "CenterBias",
                0.4f,
                "Lerp strength per second toward map centre during the dive. "
                    + "Higher = tighter spiral, lower = nearly straight. Default 0.4."
            );

            AC130MaydayCamYawLimit = cfg.Bind(
                "AC130Mayday",
                "CamYawLimit",
                25f,
                "How many degrees left/right the player can look during mayday."
            );

            AC130MaydayCamPitchLimit = cfg.Bind(
                "AC130Mayday",
                "CamPitchLimit",
                15f,
                "How many degrees up/down the player can look during mayday."
            );

            AC130MaydayShakeBase = cfg.Bind(
                "AC130Mayday",
                "ShakeBase",
                0.3f,
                "Camera shake intensity at the start of the mayday dive."
            );

            AC130MaydayShakeMax = cfg.Bind(
                "AC130Mayday",
                "ShakeMax",
                2.5f,
                "Maximum camera shake intensity at the end of the dive."
            );

            AC130MaydayExplosionScale = cfg.Bind(
                "AC130Mayday",
                "ExplosionScale",
                4.0f,
                "Explosion scale multiplier for the mayday crash. Affects blast radius, knockback, and VFX size."
            );

            AC130MaydayExplosionDuration = cfg.Bind(
                "AC130Mayday",
                "ExplosionDuration",
                12f,
                "How long (seconds) the crash explosion VFX lingers before being destroyed."
            );

            AC130MaydayRollTurnRate = cfg.Bind(
                "AC130Mayday",
                "RollTurnRate",
                45f,
                "Degrees per second of yaw the aircraft gains when fully banked (90° roll). "
                    + "Controls how tightly rolling steers the plane left/right."
            );

            AC130HitsToMayday = cfg.Bind(
                "AC130Mayday",
                "HitsToMayday",
                1f,
                "Number of rocket hits required to force the gunship into mayday. "
                    + "Only counts hits during an active session. Set to 0 to disable."
            );

            AC130RocketProximityFuse = cfg.Bind(
                "AC130Mayday",
                "RocketProximityFuse",
                4f,
                "Distance in metres at which a homing rocket detonates near the gunship. "
                    + "Must be less than 5 m so the explosion's overlap sphere (radius 5 m) "
                    + "still reaches the gunship and registers the hit."
            );

            // --- Low Gravity ---
            LowGravityGiveKey = cfg.Bind(
                "LowGravity",
                "GiveKey",
                Key.Numpad0,
                "Debug key to add the Low Gravity item to your inventory."
            );

            LowGravityUses = cfg.Bind(
                "LowGravity",
                "Uses",
                1f,
                "Number of uses per Low Gravity pickup."
            );

            LowGravityDuration = cfg.Bind(
                "LowGravity",
                "Duration",
                20f,
                "Seconds the reduced gravity lasts before physics is restored."
            );

            LowGravityScale = cfg.Bind(
                "LowGravity",
                "GravityScale",
                0.25f,
                "Fraction of normal gravity applied during the effect (e.g. 0.25 = 25%). "
                    + "Affects golf balls (Rigidbody) and player fall/jump height equally, "
                    + "since PlayerMovement reads Physics.gravity directly."
            );

            LowGravitySpawnWeight = cfg.Bind(
                "ItemBoxSpawns",
                "LowGravityWeight",
                2f,
                "Spawn weight for the Low Gravity item in item boxes. Set to 0 to disable."
            );

            // --- Sniper Rifle ---
            SniperRifleGiveKey = cfg.Bind(
                "SniperRifle",
                "GiveKey",
                Key.Numpad1,
                "Debug key to add the Sniper Rifle to your inventory."
            );

            SniperRifleUses = cfg.Bind(
                "SniperRifle",
                "Uses",
                1f,
                "Number of shots per Sniper Rifle pickup."
            );

            SniperRifleSpawnWeight = cfg.Bind(
                "ItemBoxSpawns",
                "SniperRifleWeight",
                2f,
                "Spawn weight for the Sniper Rifle in item boxes. Set to 0 to disable."
            );

            SniperRifleMaxAimingDistance = cfg.Bind(
                "SniperRifle",
                "MaxAimingDistance",
                1000f,
                "Maximum distance (units) the aim-point raycast travels when computing where the barrel points."
            );

            SniperRifleMaxShotDistance = cfg.Bind(
                "SniperRifle",
                "MaxShotDistance",
                1000f,
                "Maximum distance (units) the bullet raycast travels before missing."
            );

            SniperRifleScopedInaccuracy = cfg.Bind(
                "SniperRifle",
                "ScopedInaccuracy",
                0.05f,
                "Maximum random angular deviation (degrees) when firing while scoped. Lower = more precise."
            );

            SniperRifleHipFireInaccuracy = cfg.Bind(
                "SniperRifle",
                "HipFireInaccuracy",
                3.0f,
                "Maximum random angular deviation (degrees) when firing from the hip (not scoped)."
            );

            SniperRifleZoomFov = cfg.Bind(
                "SniperRifle",
                "ZoomFov",
                15f,
                "Camera field of view while the scope is active. Lower values zoom in more."
            );

            SniperRifleZoomSpeed = cfg.Bind(
                "SniperRifle",
                "ZoomSpeed",
                10f,
                "Speed at which the camera lerps to and from the scoped FOV."
            );

            SniperRifleShotDuration = cfg.Bind(
                "SniperRifle",
                "ShotDuration",
                0.6f,
                "Seconds the shot animation plays before the item use state resets."
            );

            SniperRifleMinZoomFov = cfg.Bind(
                "SniperRifle",
                "MinZoomFov",
                5f,
                "Minimum FOV reachable by scrolling in (maximum zoom). Lower = more zoomed."
            );

            SniperRifleMaxZoomFov = cfg.Bind(
                "SniperRifle",
                "MaxZoomFov",
                40f,
                "Maximum FOV reachable by scrolling out (minimum zoom). Must be above MinZoomFov."
            );

            SniperRifleScrollSensitivity = cfg.Bind(
                "SniperRifle",
                "ScrollSensitivity",
                5f,
                "FOV units changed per scroll notch. Higher values zoom faster."
            );

            // --- Donut ---
            DonutGiveKey = cfg.Bind(
                "Donut",
                "GiveKey",
                Key.Numpad2,
                "Debug key to add the Donut to your inventory."
            );

            DonutUses = cfg.Bind("Donut", "Uses", 1f, "Number of Donut uses per pickup.");

            DonutSpawnWeight = cfg.Bind(
                "ItemBoxSpawns",
                "DonutWeight",
                1f,
                "Spawn weight for the Donut in item boxes. Set to 0 to disable."
            );

            DonutSpeed = cfg.Bind(
                "Donut",
                "Speed",
                30f,
                "Horizontal movement speed of the Donut in units per second."
            );

            DonutAltitude = cfg.Bind(
                "Donut",
                "Altitude",
                20f,
                "Height above terrain the Donut hovers at."
            );

            DonutTerrainFollowSpeed = cfg.Bind(
                "Donut",
                "TerrainFollowSpeed",
                5f,
                "How quickly the Donut adjusts its altitude to match terrain changes (lerp speed)."
            );

            DonutTurnSpeed = cfg.Bind(
                "Donut",
                "TurnSpeed",
                8f,
                "How quickly the Donut rotates to face the movement direction (Slerp speed)."
            );

            DonutCameraPitch = cfg.Bind(
                "Donut",
                "CameraPitch",
                60f,
                "Camera pitch angle in degrees during the Donut session (0 = horizontal, 90 = straight down)."
            );

            DonutCameraDistance = cfg.Bind(
                "Donut",
                "CameraDistance",
                40f,
                "Camera distance addition from the Donut during the session."
            );

            DonutMouseSensitivity = cfg.Bind(
                "Donut",
                "MouseSensitivity",
                0.2f,
                "Mouse X sensitivity for rotating the Donut orbit camera."
            );

            DonutDuration = cfg.Bind(
                "Donut",
                "Duration",
                30f,
                "How many seconds the Donut session lasts before automatically ending."
            );

            DonutLaserUses = cfg.Bind(
                "Donut",
                "LaserUses",
                3f,
                "How many orbital laser strikes the player can fire during a Donut session."
            );

            DonutLaserAnticipationDuration = cfg.Bind(
                "Donut",
                "LaserAnticipationDuration",
                1.5f,
                "Seconds of anticipation before the orbital laser fires. The laser tracks the Donut during this window."
            );

            DonutLaserCooldown = cfg.Bind(
                "Donut",
                "LaserCooldown",
                3f,
                "Minimum seconds between orbital laser fires."
            );

            DonutHitsToDestroy = cfg.Bind(
                "Donut",
                "HitsToDestroy",
                1f,
                "Rocket hits required to shoot down the Donut. Set to 0 to make it invincible."
            );

            DonutCrashImpactForce = cfg.Bind(
                "Donut",
                "CrashImpactForce",
                500f,
                "Impulse force applied to the Donut in the direction of the rocket hit when shot down."
            );

            DonutCrashDownwardForce = cfg.Bind(
                "Donut",
                "CrashDownwardForce",
                15f,
                "Impulse force applied to the Donut in the downward direction."
            );

            DonutCrashTorque = cfg.Bind(
                "Donut",
                "CrashTorque",
                1.2f,
                "Magnitude of the random tumble torque applied to the Donut when shot down."
            );

            DonutCrashExplosionScale = cfg.Bind(
                "Donut",
                "CrashExplosionScale",
                4.0f,
                "Explosion scale multiplier for when a Donut crashes. Affects blast radius, knockback, and VFX size."
            );
            // --- Javelin ---
            JavelinGiveKey = cfg.Bind(
                "Javelin",
                "GiveKey",
                Key.Numpad3,
                "Debug key to add the Javelin to your inventory."
            );

            JavelinUses = cfg.Bind("Javelin", "Uses", 1f, "Number of Javelin uses per pickup.");

            JavelinSpawnWeight = cfg.Bind(
                "ItemBoxSpawns",
                "JavelinWeight",
                2f,
                "Spawn weight for the Javelin in item boxes. Set to 0 to disable."
            );

            JavelinApexHeight = cfg.Bind(
                "Javelin",
                "ApexHeight",
                80f,
                "How many units above the launch point the rocket climbs before turning."
            );

            JavelinAscentSpeed = cfg.Bind(
                "Javelin",
                "AscentSpeed",
                35f,
                "Speed in units per second during the upward climb phase."
            );

            JavelinDiveSpeed = cfg.Bind(
                "Javelin",
                "DiveSpeed",
                55f,
                "Initial speed in units per second when the rocket begins its dive."
            );

            JavelinDiveAcceleration = cfg.Bind(
                "Javelin",
                "DiveAcceleration",
                25f,
                "Additional speed gained per second during the dive phase."
            );

            JavelinArrivalRadius = cfg.Bind(
                "Javelin",
                "ArrivalRadius",
                3f,
                "Distance from the target position at which the rocket detonates."
            );

            JavelinTimeout = cfg.Bind(
                "Javelin",
                "Timeout",
                20f,
                "Maximum seconds before the rocket force-detonates if it hasn't hit yet."
            );

            JavelinExplosionVfxDuration = cfg.Bind(
                "Javelin",
                "ExplosionVfxDuration",
                5f,
                "Seconds before the Javelin explosion VFX prefab is destroyed on each client."
            );

            JavelinExplosionScale = cfg.Bind(
                "Explosions",
                "JavelinScale",
                3.5f,
                "Multiplier for Javelin explosions. Affects blast radius, knockback, and VFX size."
            );

            // --- StickyGrenade ---
            StickyGrenadeGiveKey = cfg.Bind(
                "StickyGrenade",
                "GiveKey",
                Key.Numpad4,
                "Debug key to add the StickyGrenade grenade to your inventory."
            );

            StickyGrenadeUses = cfg.Bind(
                "StickyGrenade",
                "Uses",
                2f,
                "Number of StickyGrenade grenades per pickup."
            );

            StickyGrenadeSpawnWeight = cfg.Bind(
                "ItemBoxSpawns",
                "StickyGrenadeWeight",
                3f,
                "Spawn weight for the StickyGrenade in item boxes. Set to 0 to disable."
            );

            StickyGrenadeThrowSpeed = cfg.Bind(
                "StickyGrenade",
                "ThrowSpeed",
                22f,
                "Initial speed in units per second when the grenade is thrown."
            );

            StickyGrenadeMaxThrowSpeed = cfg.Bind(
                "StickyGrenade",
                "MaxThrowSpeed",
                35f,
                "Server-side cap on throw speed to prevent exploits."
            );

            StickyGrenadeLobAngle = cfg.Bind(
                "StickyGrenade",
                "LobAngle",
                0.25f,
                "Upward component added to the throw direction for a natural lob arc. "
                    + "0 = perfectly flat, 1 = 45 degrees upward."
            );

            StickyGrenadeFuseTime = cfg.Bind(
                "StickyGrenade",
                "FuseTime",
                3.5f,
                "Seconds from when the grenade sticks until it detonates."
            );

            StickyGrenadeGraceTime = cfg.Bind(
                "StickyGrenade",
                "GraceTime",
                0.35f,
                "Seconds after throwing before the grenade can stick to anything. "
                    + "Prevents the grenade from immediately sticking to the thrower."
            );

            StickyGrenadeStickRadius = cfg.Bind(
                "StickyGrenade",
                "StickRadius",
                0.55f,
                "Radius of the overlap sphere used to detect stick targets each FixedUpdate."
            );

            StickyGrenadeExplosionScale = cfg.Bind(
                "Explosions",
                "StickyGrenadeScale",
                4.0f,
                "Multiplier for StickyGrenade explosions. Affects blast radius, knockback, and VFX size."
            );

            // --- Nuke ---
            NukeGiveKey = cfg.Bind(
                "Nuke",
                "GiveKey",
                Key.Numpad6,
                "Debug key to add the Nuke to your inventory."
            );

            NukeUses = cfg.Bind("Nuke", "Uses", 1f, "Number of uses per Nuke pickup.");

            NukeSpawnWeight = cfg.Bind(
                "ItemBoxSpawns",
                "NukeWeight",
                0.5f,
                "Spawn weight for the Nuke in item boxes. Set to 0 to disable."
            );

            NukeDropHeight = cfg.Bind(
                "Nuke",
                "DropHeight",
                300f,
                "Units above the map centre at which the nuke bomb spawns."
            );

            NukeDropSpeed = cfg.Bind(
                "Nuke",
                "DropSpeed",
                80f,
                "Downward speed of the falling nuke bomb in units per second."
            );

            NukeExplosionScale = cfg.Bind(
                "Explosions",
                "NukeScale",
                8.0f,
                "Explosion scale multiplier for the Nuke's detonation rocket. "
                    + "Affects blast radius, knockback force, and VFX size."
            );

            NukeSkyBlastForce = cfg.Bind(
                "Nuke",
                "SkyBlastForce",
                100f,
                "Extra upward impulse force applied to all rigidbodies within SkyBlastRadius "
                    + "after detonation. Stacks on top of the standard explosion knockback."
            );

            NukeSkyBlastRadius = cfg.Bind(
                "Nuke",
                "SkyBlastRadius",
                300f,
                "Radius (units) of the secondary sky blast. Should be large enough to "
                    + "cover the whole map so no one escapes."
            );

            NukeSkyBlastVerticalBias = cfg.Bind(
                "Nuke",
                "SkyBlastVerticalBias",
                0.6f,
                "Controls how much of the sky blast force goes upward versus outward (0 = all outward, 1 = all straight up). "
                    + "The horizontal direction is still relative to the explosion point, so players are pushed away from the blast site."
            );

            NukeExplosionVfxDuration = cfg.Bind(
                "Nuke",
                "ExplosionVfxDuration",
                8f,
                "Seconds before the nuke explosion VFX prefab is destroyed on each client."
            );

            NukeExplosionVfxScale = cfg.Bind(
                "Nuke",
                "ExplosionVfxScale",
                1.0f,
                "Uniform scale applied to the nuke explosion VFX transform on each client. "
                    + "Increase this to make the particle systems appear larger."
            );

            NukeExcludeThrower = cfg.Bind(
                "Nuke",
                "ExcludeThrower",
                false,
                "If true, the sky blast does not apply force to the player who activated the nuke."
            );

            // --- Black Hole Grenade ---
            BlackHoleGrenadeGiveKey = cfg.Bind(
                "BlackHoleGrenade",
                "GiveKey",
                Key.Numpad7,
                "Debug key to add the Black Hole Grenade to your inventory."
            );

            BlackHoleGrenadeUses = cfg.Bind(
                "BlackHoleGrenade",
                "Uses",
                1f,
                "Number of uses per Black Hole Grenade pickup."
            );

            BlackHoleGrenadeSpawnWeight = cfg.Bind(
                "ItemBoxSpawns",
                "BlackHoleGrenadeWeight",
                0.8f,
                "Spawn weight for the Black Hole Grenade in item boxes. Set to 0 to disable."
            );

            BlackHoleGrenadeThrowSpeed = cfg.Bind(
                "BlackHoleGrenade",
                "ThrowSpeed",
                20f,
                "Initial throw speed in m/s."
            );

            BlackHoleGrenadeMaxThrowSpeed = cfg.Bind(
                "BlackHoleGrenade",
                "MaxThrowSpeed",
                30f,
                "Server-side clamp on throw speed to prevent exploits."
            );

            BlackHoleGrenadeLobAngle = cfg.Bind(
                "BlackHoleGrenade",
                "LobAngle",
                0.4f,
                "Upward component added to the throw direction to create an arc. "
                    + "0 = flat, 1 = 45° upward."
            );

            BlackHoleGrenadeGraceTime = cfg.Bind(
                "BlackHoleGrenade",
                "GraceTime",
                0.35f,
                "Seconds after throwing before the grenade can stick to the ground."
            );

            BlackHoleGrenadeSuckDuration = cfg.Bind(
                "BlackHoleGrenade",
                "SuckDuration",
                6f,
                "How many seconds the suction phase lasts before spitting."
            );

            BlackHoleGrenadeSuckRadius = cfg.Bind(
                "BlackHoleGrenade",
                "SuckRadius",
                35f,
                "Radius in units of the suction field."
            );

            BlackHoleGrenadeSuckForce = cfg.Bind(
                "BlackHoleGrenade",
                "SuckForce",
                8f,
                "Suction acceleration (m/s²) applied at the outer edge of the suction field."
            );

            BlackHoleGrenadeMaxSuckForce = cfg.Bind(
                "BlackHoleGrenade",
                "MaxSuckForce",
                40f,
                "Suction acceleration (m/s²) applied right at the center of the black hole."
            );

            BlackHoleGrenadeSpitForce = cfg.Bind(
                "BlackHoleGrenade",
                "SpitForce",
                35f,
                "Speed (m/s) at which objects and players are ejected during the spit phase."
            );

            BlackHoleGrenadeSpitVfxScale = cfg.Bind(
                "BlackHoleGrenade",
                "SpitVfxScale",
                3f,
                "Scale of the explosion VFX played on all clients when the black hole collapses."
            );

            BlackHoleGrenadeKnockdownRadius = cfg.Bind(
                "BlackHoleGrenade",
                "KnockdownRadius",
                15f,
                "Distance from the black hole at which a player gets knocked down. "
                    + "Must be less than SuckRadius. Set to 0 to disable."
            );

            BlackHoleGrenadeExcludeThrower = cfg.Bind(
                "BlackHoleGrenade",
                "ExcludeThrower",
                false,
                "If true, the player who threw the grenade is not affected by suction or spit."
            );

            BlackHoleGrenadeBonusGolfCartForceMultiplier = cfg.Bind(
                "BlackHoleGrenade",
                "BonusGolfCartForceMultiplier",
                2.0f,
                "Additional force multiplier applied to golf carts."
            );

            BlackHoleGrenadeAffectsGolfBalls = cfg.Bind(
                "BlackHoleGrenade",
                "AffectsGolfBalls",
                true,
                "If true, the black hole grenade sucks in and spits out golf balls. Set to false to leave golf balls unaffected."
            );

            // --- Placeable Wall ---
            PlaceableWallGiveKey = cfg.Bind(
                "PlaceableWall",
                "GiveKey",
                Key.Numpad6,
                "Debug key to add the Placeable Wall to your inventory."
            );

            PlaceableWallUses = cfg.Bind(
                "PlaceableWall",
                "Uses",
                1f,
                "Number of wall placements per pickup."
            );

            PlaceableWallSpawnWeight = cfg.Bind(
                "ItemBoxSpawns",
                "PlaceableWallWeight",
                2f,
                "Spawn weight for the Placeable Wall in item boxes. Set to 0 to disable."
            );

            PlaceableWallMaxPlacementDistance = cfg.Bind(
                "PlaceableWall",
                "MaxPlacementDistance",
                20f,
                "Maximum distance (units) from the player's camera at which a wall can be placed."
            );

            PlaceableWallMinHoleDistance = cfg.Bind(
                "PlaceableWall",
                "MinHoleDistance",
                3f,
                "Minimum XZ distance (units) from the hole centre that a wall may be placed. "
                    + "Prevents blocking the hole entirely. Set to 0 to disable the check."
            );

            PlaceableWallHealthPoints = cfg.Bind(
                "PlaceableWall",
                "HealthPoints",
                3f,
                "Number of golf club or baseball bat hits required to destroy the wall. "
                    + "Rocket hits always destroy in one shot."
            );

            PlaceableWallVelocityImpactFactor = cfg.Bind(
                "PlaceableWall",
                "VelocityImpactFactor",
                2.5f,
                "Degree to which the velocity of an object colliding with the placeable wall will damage it. Default: 2.5."
            );

            PlaceableWallTorsionMultiplier = cfg.Bind(
                "PlaceableWall",
                "TorsionMultiplier",
                0.002f,
                "Degree to which torque (spin) damages the bricks of the placeable wall. Default: 0.002."
            );

            PlaceableWallRotationStep = cfg.Bind(
                "PlaceableWall",
                "RotationStep",
                45f,
                "Degrees the wall rotates per scroll-wheel tick during the placement preview. "
                    + "Scroll up rotates clockwise, scroll down rotates counter-clockwise."
            );

            PlaceableWallDebrisLifetime = cfg.Bind(
                "PlaceableWall",
                "DebrisLifetime",
                30f,
                "Seconds before detached wall debris (bricks/pillars) are automatically destroyed."
            );

            PlaceableWallRocketExplosionForce = cfg.Bind(
                "PlaceableWall",
                "RocketExplosionForce",
                600f,
                "Impulse force (per brick) applied by a rocket explosion to detached wall debris."
            );

            PlaceableWallDamageGolfClub = cfg.Bind(
                "PlaceableWall",
                "DamageGolfClub",
                1f,
                "Damage dealt to a wall chunk per golf club swing."
            );

            PlaceableWallDamageBaseballBat = cfg.Bind(
                "PlaceableWall",
                "DamageBaseballBat",
                2f,
                "Damage dealt to a wall chunk per baseball bat swing."
            );

            // --- Sub-Machine Gun ---
            SubMachineGunGiveKey = cfg.Bind(
                "SubMachineGun",
                "GiveKey",
                Key.M,
                "Debug key to add the Sub-Machine Gun to your inventory."
            );
            SubMachineGunUses = cfg.Bind(
                "SubMachineGun",
                "Uses",
                30f,
                "Total bullets per Sub-Machine Gun pickup. Each shot consumes one use."
            );
            SubMachineGunSpawnWeight = cfg.Bind(
                "ItemBoxSpawns",
                "SubMachineGunWeight",
                2f,
                "Spawn weight for the Sub-Machine Gun in item boxes. Set to 0 to disable."
            );
            SubMachineGunFireRate = cfg.Bind(
                "SubMachineGun",
                "FireRate",
                0.08f,
                "Seconds between each bullet in the burst."
            );
            SubMachineGunInaccuracy = cfg.Bind(
                "SubMachineGun",
                "Inaccuracy",
                22f,
                "Random spread angle (degrees) applied to each bullet. Higher = less accurate."
            );
            SubMachineGunMaxAimingDistance = cfg.Bind(
                "SubMachineGun",
                "MaxAimingDistance",
                500f,
                "Max distance used when computing the aim point for each bullet."
            );
            SubMachineGunMaxShotDistance = cfg.Bind(
                "SubMachineGun",
                "MaxShotDistance",
                500f,
                "Max raycast distance for each bullet."
            );

            // --- Bear ---
            BearGiveKey = cfg.Bind(
                "Bear",
                "GiveKey",
                Key.Numpad5,
                "Debug key to add the Bear item to your inventory."
            );
            BearUses = cfg.Bind("Bear", "Uses", 1f, "Number of uses per Bear item pickup.");
            BearSpawnWeight = cfg.Bind(
                "ItemBoxSpawns",
                "BearWeight",
                2f,
                "Spawn weight for the Bear item in item boxes. Set to 0 to disable."
            );
            BearCount = cfg.Bind("Bear", "BearCount", 2f, "Number of bears spawned per item use.");
            BearSpawnRadius = cfg.Bind(
                "Bear",
                "SpawnRadius",
                15f,
                "Radius around the player within which bears spawn."
            );
            BearSessionDuration = cfg.Bind(
                "Bear",
                "SessionDuration",
                60f,
                "Seconds before all remaining bears are forcibly despawned."
            );
            BearWalkSpeed = cfg.Bind(
                "Bear",
                "WalkSpeed",
                4f,
                "Bear movement speed while wandering or obstructed (units/sec)."
            );
            BearRunSpeed = cfg.Bind(
                "Bear",
                "RunSpeed",
                9f,
                "Bear movement speed while pursuing a target (units/sec)."
            );
            BearChargeSpeed = cfg.Bind(
                "Bear",
                "ChargeSpeed",
                14f,
                "Bear movement speed during the committed charge phase (units/sec)."
            );
            BearTurnSpeed = cfg.Bind(
                "Bear",
                "TurnSpeed",
                6f,
                "How quickly the bear rotates to face its movement direction."
            );
            BearWanderRadius = cfg.Bind(
                "Bear",
                "WanderRadius",
                12f,
                "Radius of the area the bear wanders within when idle."
            );
            BearChargeRange = cfg.Bind(
                "Bear",
                "ChargeRange",
                10f,
                "Distance at which the bear commits to a charge attack (units)."
            );
            BearAttackRange = cfg.Bind(
                "Bear",
                "AttackRange",
                2.8f,
                "Distance at which the bear's attack swing connects (units)."
            );
            BearAttackCooldown = cfg.Bind(
                "Bear",
                "AttackCooldown",
                1.8f,
                "Seconds between the end of one attack and the start of the next pursuit."
            );
            BearAttackAnimationDuration = cfg.Bind(
                "Bear",
                "AttackAnimationDuration",
                1.2f,
                "Total duration of the attack animation. Hit is applied at 55% of this value."
            );
            BearSpawnAnimationDuration = cfg.Bind(
                "Bear",
                "SpawnAnimationDuration",
                2.0f,
                "Duration of the Buff/spawn-in animation before the bear begins hunting."
            );
            BearDeathAnimationDuration = cfg.Bind(
                "Bear",
                "DeathAnimationDuration",
                2.5f,
                "How long the death animation plays before the bear is destroyed."
            );
            BearStunDuration = cfg.Bind(
                "Bear",
                "StunDuration",
                2.0f,
                "Seconds the bear is stunned after being hit by an explosion."
            );
            BearEnrageDuration = cfg.Bind(
                "Bear",
                "EnrageDuration",
                5f,
                "Seconds the bear runs at enrage speed after recovering from a stun."
            );
            BearEnrageSpeedMultiplier = cfg.Bind(
                "Bear",
                "EnrageSpeedMultiplier",
                1.5f,
                "Speed multiplier applied to RunSpeed during enrage."
            );
            BearMaxHP = cfg.Bind(
                "Bear",
                "MaxHP",
                100f,
                "Maximum HP for a bear. Each hit reduces HP by the weapon's damage value; bear dies at 0."
            );
            BearDamageDuelingPistol = cfg.Bind(
                "Bear",
                "DamageDuelingPistol",
                25f,
                "HP damage dealt to a bear by a dueling pistol shot."
            );
            BearDamageElephantGun = cfg.Bind(
                "Bear",
                "DamageElephantGun",
                50f,
                "HP damage dealt to a bear by an elephant gun shot."
            );
            BearDamageRocketDirect = cfg.Bind(
                "Bear",
                "DamageRocketDirect",
                100f,
                "HP damage dealt to a bear by a direct rocket hit (explosion centre within 1.5 units)."
            );
            BearDamageRocketExplosion = cfg.Bind(
                "Bear",
                "DamageRocketExplosion",
                35f,
                "HP damage dealt to a bear by rocket splash damage (not a direct hit)."
            );
            BearDamageGolfClub = cfg.Bind(
                "Bear",
                "DamageGolfClub",
                25f,
                "HP damage dealt to a bear by a golf club swing."
            );
            BearDamageBaseballBat = cfg.Bind(
                "Bear",
                "DamageBaseballBat",
                40f,
                "HP damage dealt to a bear by a baseball bat swing."
            );
            BearDamageOrbitalLaser = cfg.Bind(
                "Bear",
                "DamageOrbitalLaser",
                100f,
                "HP damage dealt to a bear by the orbital laser."
            );
            BearMeleeKnockbackForce = cfg.Bind(
                "Bear",
                "MeleeKnockbackForce",
                12f,
                "Impulse force applied to a bear's rigidbody when hit by a golf club swing."
            );
            BearBatKnockbackForce = cfg.Bind(
                "Bear",
                "BatKnockbackForce",
                22f,
                "Impulse force applied to a bear's rigidbody when hit by a baseball bat swing."
            );
            BearMaxClimbHeight = cfg.Bind(
                "Bear",
                "MaxClimbHeight",
                6f,
                "Max height difference (units) before a target is considered unreachable."
            );
            BearTargetLockDuration = cfg.Bind(
                "Bear",
                "TargetLockDuration",
                8f,
                "Minimum seconds a bear keeps its target before re-evaluating."
            );
            BearTargetStealThreshold = cfg.Bind(
                "Bear",
                "TargetStealThreshold",
                12f,
                "A new candidate must be this many units closer to steal the lock after the timer expires."
            );
            BearTargetAbandonDistance = cfg.Bind(
                "Bear",
                "TargetAbandonDistance",
                65f,
                "If the locked target moves beyond this distance the lock is dropped immediately."
            );
            BearAggroStealThreshold = cfg.Bind(
                "Bear",
                "AggroStealThreshold",
                4f,
                "A player who hit the bear can steal the lock if at least this many units closer (lower bar than normal steal)."
            );
            BearAggroDuration = cfg.Bind(
                "Bear",
                "AggroDuration",
                15f,
                "How long (seconds) aggro on a specific player lasts after they hit the bear."
            );
            BearMeleeHitRange = cfg.Bind(
                "Bear",
                "MeleeHitRange",
                2.5f,
                "Radius (units) around the swing hitbox centre that counts as a golf-club/bat hit on a bear."
            );
            BearFriendlyFire = cfg.Bind(
                "Bear",
                "FriendlyFire",
                false,
                "If false, bears will not target or attack the player who summoned them."
            );
        }
    }
}
