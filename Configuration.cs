using BepInEx.Configuration;
using UnityEngine.InputSystem;

namespace IssaPlugin
{
    public static class Configuration
    {
        // --- Baseball Bat ---
        public static ConfigEntry<float> BaseballBatPowerMultiplier { get; private set; }
        public static ConfigEntry<int> BaseballBatUses { get; private set; }
        public static ConfigEntry<Key> BaseballBatGiveKey { get; private set; }
        public static ConfigEntry<float> BaseballBatSpawnWeight { get; private set; }

        // --- Stealth Bomber ---
        public static ConfigEntry<float> BomberAltitude { get; private set; }
        public static ConfigEntry<float> BomberSpeed { get; private set; }
        public static ConfigEntry<float> BomberRocketInterval { get; private set; }
        public static ConfigEntry<float> BomberSpread { get; private set; }
        public static ConfigEntry<int> BomberUses { get; private set; }
        public static ConfigEntry<Key> BomberGiveKey { get; private set; }
        public static ConfigEntry<float> BomberWaitTime { get; private set; }
        public static ConfigEntry<float> BomberStripLength { get; private set; }
        public static ConfigEntry<float> BomberRocketAngularJitter { get; private set; }
        public static ConfigEntry<float> BomberTargetingZoomSpeed { get; private set; }
        public static ConfigEntry<float> BomberTargetMoveSpeed { get; private set; }
        public static ConfigEntry<float> BomberTargetRotateSpeed { get; private set; }
        public static ConfigEntry<float> BomberSpawnWeight { get; private set; }
        public static ConfigEntry<float> BomberApproachDistance { get; private set; }

        // --- Predator Missile ---
        public static ConfigEntry<float> MissileAltitude { get; private set; }
        public static ConfigEntry<float> MissileFallSpeed { get; private set; }
        public static ConfigEntry<float> MissileSteerSpeed { get; private set; }
        public static ConfigEntry<float> MissileTimeout { get; private set; }
        public static ConfigEntry<int> MissileUses { get; private set; }
        public static ConfigEntry<Key> MissileGiveKey { get; private set; }
        public static ConfigEntry<float> MissileSpawnWeight { get; private set; }

        // --- AC130 ---
        public static ConfigEntry<int> AC130Uses { get; private set; }
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
                99,
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

            BomberUses = cfg.Bind("StealthBomber", "Uses", 1, "Number of bombing runs per pickup.");

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
                1,
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

            AC130Uses = cfg.Bind("AC130", "Uses", 1, "Number of AC130 uses per pickup.");

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

            AC130SpawnWeight = cfg.Bind(
                "ItemBoxSpawns",
                "AC130Weight",
                1f,
                "Spawn weight for the AC130 in item boxes. Set to 0 to disable."
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

            AC130YawLimit = cfg.Bind(
                "AC130",
                "YawLimit",
                40f,
                "How many degrees left/right the player can pan from the map centre."
            );

            IssaPluginPlugin.Log.LogInfo("Configuration initialized.");
        }
    }
}
