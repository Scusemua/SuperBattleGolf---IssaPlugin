using BepInEx.Configuration;
using UnityEngine.InputSystem;

namespace IssaPlugin
{
    public static class Configuration
    {
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

        // --- Freeze World ---
        public static ConfigEntry<Key> FreezeGiveKey { get; private set; }
        public static ConfigEntry<float> FreezeUses { get; private set; }
        public static ConfigEntry<float> FreezeDuration { get; private set; }
        public static ConfigEntry<float> FreezeFriction { get; private set; }
        public static ConfigEntry<float> FreezeBounciness { get; private set; }
        public static ConfigEntry<float> FreezeCartSidewaysStiffness { get; private set; }
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
        public static ConfigEntry<float> SniperRifleZoomFov           { get; private set; }
        public static ConfigEntry<float> SniperRifleZoomSpeed         { get; private set; }
        public static ConfigEntry<float> SniperRifleShotDuration      { get; private set; }
        public static ConfigEntry<float> SniperRifleMinZoomFov        { get; private set; }
        public static ConfigEntry<float> SniperRifleMaxZoomFov        { get; private set; }
        public static ConfigEntry<float> SniperRifleScrollSensitivity { get; private set; }

        // --- UFO ---
        public static ConfigEntry<Key> UFOGiveKey { get; private set; }
        public static ConfigEntry<float> UFOUses { get; private set; }
        public static ConfigEntry<float> UFOSpawnWeight { get; private set; }
        public static ConfigEntry<float> UFOSpeed { get; private set; }
        public static ConfigEntry<float> UFOAltitude { get; private set; }
        public static ConfigEntry<float> UFOTerrainFollowSpeed { get; private set; }
        public static ConfigEntry<float> UFOTurnSpeed { get; private set; }
        public static ConfigEntry<float> UFOCameraPitch { get; private set; }
        public static ConfigEntry<float> UFOCameraDistance { get; private set; }
        public static ConfigEntry<float> UFOMouseSensitivity { get; private set; }
        public static ConfigEntry<float> UFODuration { get; private set; }
        public static ConfigEntry<float> UFOLaserUses { get; private set; }
        public static ConfigEntry<float> UFOLaserAnticipationDuration { get; private set; }
        public static ConfigEntry<float> UFOLaserCooldown { get; private set; }

        // --- Explosion Scaling ---
        public static ConfigEntry<float> AC130ExplosionScale { get; private set; }
        public static ConfigEntry<float> PredatorMissileExplosionScale { get; private set; }
        public static ConfigEntry<float> StealthBomberExplosionScale { get; private set; }

        public static void Initialize(ConfigFile cfg)
        {
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
                "Surface bounciness applied to all physics contacts during a freeze."
            );

            FreezeCartSidewaysStiffness = cfg.Bind(
                "FreezeWorld",
                "CartSidewaysStiffness",
                0.15f,
                "Sideways friction stiffness for golf cart wheel colliders while frozen (0 = no grip, 1 = normal). Lower values cause more drift."
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

            // --- UFO ---
            UFOGiveKey = cfg.Bind(
                "UFO",
                "GiveKey",
                Key.Numpad2,
                "Debug key to add the UFO to your inventory."
            );

            UFOUses = cfg.Bind("UFO", "Uses", 1f, "Number of UFO uses per pickup.");

            UFOSpawnWeight = cfg.Bind(
                "ItemBoxSpawns",
                "UFOWeight",
                1f,
                "Spawn weight for the UFO in item boxes. Set to 0 to disable."
            );

            UFOSpeed = cfg.Bind(
                "UFO",
                "Speed",
                30f,
                "Horizontal movement speed of the UFO in units per second."
            );

            UFOAltitude = cfg.Bind(
                "UFO",
                "Altitude",
                20f,
                "Height above terrain the UFO hovers at."
            );

            UFOTerrainFollowSpeed = cfg.Bind(
                "UFO",
                "TerrainFollowSpeed",
                5f,
                "How quickly the UFO adjusts its altitude to match terrain changes (lerp speed)."
            );

            UFOTurnSpeed = cfg.Bind(
                "UFO",
                "TurnSpeed",
                8f,
                "How quickly the UFO rotates to face the movement direction (Slerp speed)."
            );

            UFOCameraPitch = cfg.Bind(
                "UFO",
                "CameraPitch",
                60f,
                "Camera pitch angle in degrees during the UFO session (0 = horizontal, 90 = straight down)."
            );

            UFOCameraDistance = cfg.Bind(
                "UFO",
                "CameraDistance",
                40f,
                "Camera distance addition from the UFO during the session."
            );

            UFOMouseSensitivity = cfg.Bind(
                "UFO",
                "MouseSensitivity",
                0.2f,
                "Mouse X sensitivity for rotating the UFO orbit camera."
            );

            UFODuration = cfg.Bind(
                "UFO",
                "Duration",
                30f,
                "How many seconds the UFO session lasts before automatically ending."
            );

            UFOLaserUses = cfg.Bind(
                "UFO",
                "LaserUses",
                3f,
                "How many orbital laser strikes the player can fire during a UFO session."
            );

            UFOLaserAnticipationDuration = cfg.Bind(
                "UFO",
                "LaserAnticipationDuration",
                1.5f,
                "Seconds of anticipation before the orbital laser fires. The laser tracks the UFO during this window."
            );

            UFOLaserCooldown = cfg.Bind(
                "UFO",
                "LaserCooldown",
                3f,
                "Minimum seconds between orbital laser fires."
            );
        }
    }
}
