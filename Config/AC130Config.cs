using BepInEx.Configuration;
using UnityEngine.InputSystem;

namespace IssaPlugin
{
    public class AC130Config
    {
        private const string Section = "AC130";
        private const string MaydaySection = "AC130Mayday";

        public ConfigEntry<float> Uses { get; private set; }
        public ConfigEntry<Key> GiveKey { get; private set; }
        public ConfigEntry<float> SpawnWeight { get; private set; }
        public ConfigEntry<float> OrbitRadius { get; private set; }
        public ConfigEntry<float> OrbitSpeed { get; private set; }
        public ConfigEntry<float> Altitude { get; private set; }
        public ConfigEntry<float> Duration { get; private set; }
        public ConfigEntry<float> CameraPitch { get; private set; }
        public ConfigEntry<float> CameraDistance { get; private set; }
        public ConfigEntry<float> FireCooldown { get; private set; }
        public ConfigEntry<float> RocketAngularJitter { get; private set; }
        public ConfigEntry<float> BoostMultiplier { get; private set; }
        public ConfigEntry<float> AltitudeOffsetMax { get; private set; }
        public ConfigEntry<float> AltitudeAdjustSpeed { get; private set; }
        public ConfigEntry<float> ZoomFov { get; private set; }
        public ConfigEntry<float> ZoomSpeed { get; private set; }
        public ConfigEntry<float> ApproachDistance { get; private set; }
        public ConfigEntry<float> ApproachSpeed { get; private set; }
        public ConfigEntry<float> BaseFov { get; private set; }
        public ConfigEntry<float> YawLimit { get; private set; }
        public ConfigEntry<float> PitchLimit { get; private set; }
        public ConfigEntry<float> MouseSensitivity { get; private set; }
        public ConfigEntry<float> HeavyFireCooldown { get; private set; }
        public ConfigEntry<float> HeavyShotsBeforeReload { get; private set; }
        public ConfigEntry<float> HeavyReloadTime { get; private set; }
        public ConfigEntry<bool> MaydayEnabled { get; private set; }
        public ConfigEntry<Key> MaydayKey { get; private set; }
        public ConfigEntry<float> MaydayDiveSteepRate { get; private set; }
        public ConfigEntry<float> MaydayInitialDiveAngle { get; private set; }
        public ConfigEntry<float> MaydayMaxDiveAngle { get; private set; }
        public ConfigEntry<float> MaydayPullInfluence { get; private set; }
        public ConfigEntry<float> MaydayRollSpeed { get; private set; }
        public ConfigEntry<float> MaydaySpeed { get; private set; }
        public ConfigEntry<float> MaydayDrift { get; private set; }
        public ConfigEntry<float> MaydayCenterBias { get; private set; }
        public ConfigEntry<float> MaydayCamYawLimit { get; private set; }
        public ConfigEntry<float> MaydayCamPitchLimit { get; private set; }
        public ConfigEntry<float> MaydayShakeBase { get; private set; }
        public ConfigEntry<float> MaydayShakeMax { get; private set; }
        public ConfigEntry<float> MaydayExplosionScale { get; private set; }
        public ConfigEntry<float> MaydayExplosionDuration { get; private set; }
        public ConfigEntry<float> MaydayRollTurnRate { get; private set; }
        public ConfigEntry<float> HitsToMayday { get; private set; }
        public ConfigEntry<float> RocketProximityFuse { get; private set; }
        public ConfigEntry<bool> ApplyCoffeeBoostAfterwards { get; private set; }
        public ConfigEntry<float> ExplosionScale { get; private set; }
        public ConfigEntry<float> HeavyRocketExplosionScale { get; private set; }

        public AC130Config(ConfigFile cfg, GlobalConfig global)
        {
            Uses = cfg.Bind(Section, "Uses", 1f, "Number of AC130 uses per pickup.");
            GiveKey = cfg.Bind(
                Section,
                "GiveKey",
                Key.F11,
                "Key to press to add the AC130 to your inventory."
            );
            OrbitRadius = cfg.Bind(
                Section,
                "OrbitRadius",
                100f,
                "Radius in units of the circle the gunship flies around the map centre."
            );
            OrbitSpeed = cfg.Bind(
                Section,
                "OrbitSpeed",
                6f,
                "Degrees per second at which the gunship orbits the map centre."
            );
            Altitude = cfg.Bind(
                Section,
                "Altitude",
                140f,
                "Height above the map centre the gunship flies at."
            );
            Duration = cfg.Bind(
                Section,
                "Duration",
                40f,
                "How many seconds the AC130 remains active before leaving."
            );
            CameraPitch = cfg.Bind(
                Section,
                "CameraPitch",
                80f,
                "Camera pitch angle in degrees during the AC130 (0 = horizontal, 90 = straight down)."
            );
            CameraDistance = cfg.Bind(
                Section,
                "CameraDistance",
                80f,
                "Camera distance addition from the gunship pivot during the AC130."
            );
            FireCooldown = cfg.Bind(
                Section,
                "FireCooldown",
                0.35f,
                "Minimum seconds between rocket fires."
            );
            HeavyFireCooldown = cfg.Bind(
                Section,
                "HeavyFireCooldown",
                3f,
                "Minimum seconds between heavy (big shot) rocket fires."
            );
            HeavyShotsBeforeReload = cfg.Bind(
                Section,
                "HeavyShotsBeforeReload",
                3f,
                "Number of big shots before the heavy rocket must reload."
            );
            HeavyReloadTime = cfg.Bind(
                Section,
                "HeavyReloadTime",
                8f,
                "Time in seconds for the heavy rocket to reload after depleting all big shots."
            );
            RocketAngularJitter = cfg.Bind(
                Section,
                "RocketAngularJitter",
                0.5f,
                "Random angular jitter in degrees applied to each rocket fired from the AC130."
            );
            BoostMultiplier = cfg.Bind(
                Section,
                "BoostMultiplier",
                2.25f,
                "Multiplier applied to orbit speed when holding Left Shift."
            );
            AltitudeOffsetMax = cfg.Bind(
                Section,
                "AltitudeOffsetMax",
                80f,
                "Maximum units the player can raise the gunship from its base altitude using Q/E."
            );
            AltitudeAdjustSpeed = cfg.Bind(
                Section,
                "AltitudeAdjustSpeed",
                10f,
                "Units per second the gunship rises or descends when holding Q or E."
            );
            ZoomFov = cfg.Bind(
                Section,
                "ZoomFov",
                20f,
                "Field of view when right-click zooming in the AC130. Lower values zoom in more (default camera FOV is typically 60)."
            );
            ZoomSpeed = cfg.Bind(
                Section,
                "ZoomSpeed",
                8f,
                "Speed at which the camera lerps to and from the zoomed FOV when right-clicking."
            );
            ApproachDistance = cfg.Bind(
                Section,
                "ApproachDistance",
                200f,
                "How far away the AC130 spawns and flies in from before reaching the orbit point."
            );
            ApproachSpeed = cfg.Bind(
                Section,
                "ApproachSpeed",
                60f,
                "Speed in units per second at which the AC130 flies in and out."
            );
            BaseFov = cfg.Bind(Section, "BaseFov", 60f, "Base field of view for the AC130 camera.");
            YawLimit = cfg.Bind(
                Section,
                "YawLimit",
                40f,
                "How many degrees left/right the player can pan from the map centre."
            );
            PitchLimit = cfg.Bind(
                Section,
                "PitchLimit",
                30f,
                "How many degrees up/down the player can pan from the map centre."
            );
            MouseSensitivity = cfg.Bind(
                Section,
                "MouseSensitivity",
                0.15f,
                "How sensitive the player's mouse is to panning the camera."
            );
            ApplyCoffeeBoostAfterwards = cfg.Bind(
                Section,
                "ApplyCoffeeBoostAfterwards",
                true,
                "Give the user of an AC130 a max coffee boost after the AC130 ends to make up for the time they spent sitting in the AC130, not golfing."
            );

            MaydayEnabled = cfg.Bind(
                MaydaySection,
                "Enabled",
                true,
                "Whether the manual mayday self-destruct hotkey is available."
            );
            MaydayKey = cfg.Bind(
                MaydaySection,
                "Key",
                Key.M,
                "Hotkey to manually trigger mayday (self-destruct) while in an AC130 session."
            );
            MaydayDiveSteepRate = cfg.Bind(
                MaydaySection,
                "DiveSteepRate",
                8f,
                "Degrees per second at which the dive pitch steepens toward vertical."
            );
            MaydayInitialDiveAngle = cfg.Bind(
                MaydaySection,
                "InitialDiveAngle",
                20f,
                "Starting pitch angle (degrees below horizontal) when mayday begins."
            );
            MaydayMaxDiveAngle = cfg.Bind(
                MaydaySection,
                "MaxDiveAngle",
                85f,
                "Maximum pitch angle (degrees below horizontal) the dive steepens to."
            );
            MaydayPullInfluence = cfg.Bind(
                MaydaySection,
                "PullInfluence",
                6f,
                "Degrees per second of pitch influence the player has when holding W/S during mayday."
            );
            MaydayRollSpeed = cfg.Bind(
                MaydaySection,
                "RollSpeed",
                45f,
                "Degrees per second the player can roll the gunship with A/D during mayday."
            );
            MaydaySpeed = cfg.Bind(
                MaydaySection,
                "Speed",
                80f,
                "Forward speed of the gunship during the mayday dive in units per second."
            );
            MaydayDrift = cfg.Bind(
                MaydaySection,
                "Drift",
                3f,
                "Maximum random lateral drift added to the dive direction per second."
            );
            MaydayCenterBias = cfg.Bind(
                MaydaySection,
                "CenterBias",
                0.4f,
                "Lerp strength per second toward map centre during the dive. Higher = tighter spiral, lower = nearly straight. Default 0.4."
            );
            MaydayCamYawLimit = cfg.Bind(
                MaydaySection,
                "CamYawLimit",
                25f,
                "How many degrees left/right the player can look during mayday."
            );
            MaydayCamPitchLimit = cfg.Bind(
                MaydaySection,
                "CamPitchLimit",
                15f,
                "How many degrees up/down the player can look during mayday."
            );
            MaydayShakeBase = cfg.Bind(
                MaydaySection,
                "ShakeBase",
                0.3f,
                "Camera shake intensity at the start of the mayday dive."
            );
            MaydayShakeMax = cfg.Bind(
                MaydaySection,
                "ShakeMax",
                2.5f,
                "Maximum camera shake intensity at the end of the dive."
            );
            MaydayExplosionScale = cfg.Bind(
                MaydaySection,
                "ExplosionScale",
                4.0f,
                "Explosion scale multiplier for the mayday crash. Affects blast radius, knockback, and VFX size."
            );
            MaydayExplosionDuration = cfg.Bind(
                MaydaySection,
                "ExplosionDuration",
                12f,
                "How long (seconds) the crash explosion VFX lingers before being destroyed."
            );
            MaydayRollTurnRate = cfg.Bind(
                MaydaySection,
                "RollTurnRate",
                45f,
                "Degrees per second of yaw the aircraft gains when fully banked (90° roll). Controls how tightly rolling steers the plane left/right."
            );
            HitsToMayday = cfg.Bind(
                MaydaySection,
                "HitsToMayday",
                1f,
                "Number of rocket hits required to force the gunship into mayday. Only counts hits during an active session. Set to 0 to disable."
            );
            RocketProximityFuse = cfg.Bind(
                MaydaySection,
                "RocketProximityFuse",
                4f,
                "Distance in metres at which a homing rocket detonates near the gunship. Must be less than 5 m so the explosion's overlap sphere (radius 5 m) still reaches the gunship and registers the hit."
            );

            SpawnWeight = global.BindSpawnWeight(
                cfg,
                103,
                "AC130Weight",
                5f,
                "Override spawn weight for the AC-130 Gunship."
            );
            ExplosionScale = cfg.Bind(
                "Explosions",
                "AC130Scale",
                2.25f,
                "Multiplier for AC130 rocket explosions. Affects blast radius, knockback, and VFX size."
            );
            HeavyRocketExplosionScale = cfg.Bind(
                "Explosions",
                "AC130HeavyRocketScale",
                5.0f,
                "Multiplier for AC130 heavy (big shot) rocket explosions. Affects blast radius, knockback, and VFX size."
            );
        }
    }
}
