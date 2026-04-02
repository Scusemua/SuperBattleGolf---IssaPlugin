using BepInEx.Configuration;
using UnityEngine.InputSystem;

namespace IssaPlugin
{
    public class SniperRifleConfig
    {
        private const string Section = "SniperRifle";

        public ConfigEntry<Key> GiveKey { get; private set; }
        public ConfigEntry<float> Uses { get; private set; }
        public ConfigEntry<float> SpawnWeight { get; private set; }
        public ConfigEntry<float> MaxAimingDistance { get; private set; }
        public ConfigEntry<float> MaxShotDistance { get; private set; }
        public ConfigEntry<float> ScopedInaccuracy { get; private set; }
        public ConfigEntry<float> HipFireInaccuracy { get; private set; }
        public ConfigEntry<float> ZoomFov { get; private set; }
        public ConfigEntry<float> ZoomSpeed { get; private set; }
        public ConfigEntry<float> ShotDuration { get; private set; }
        public ConfigEntry<float> MinZoomFov { get; private set; }
        public ConfigEntry<float> MaxZoomFov { get; private set; }
        public ConfigEntry<float> ScrollSensitivity { get; private set; }
        public ConfigEntry<float> ScreenShakeIntensity { get; private set; }

        public SniperRifleConfig(ConfigFile cfg, GlobalConfig global)
        {
            GiveKey = cfg.Bind(Section, "GiveKey", Key.Numpad1, "Debug key to add the Sniper Rifle to your inventory.");
            Uses = cfg.Bind(Section, "Uses", 1f, "Number of shots per Sniper Rifle pickup.");
            MaxAimingDistance = cfg.Bind(Section, "MaxAimingDistance", 1000f, "Maximum distance (units) the aim-point raycast travels when computing where the barrel points.");
            MaxShotDistance = cfg.Bind(Section, "MaxShotDistance", 1000f, "Maximum distance (units) the bullet raycast travels before missing.");
            ScopedInaccuracy = cfg.Bind(Section, "ScopedInaccuracy", 0.05f, "Maximum random angular deviation (degrees) when firing while scoped. Lower = more precise.");
            HipFireInaccuracy = cfg.Bind(Section, "HipFireInaccuracy", 3.0f, "Maximum random angular deviation (degrees) when firing from the hip (not scoped).");
            ZoomFov = cfg.Bind(Section, "ZoomFov", 15f, "Camera field of view while the scope is active. Lower values zoom in more.");
            ZoomSpeed = cfg.Bind(Section, "ZoomSpeed", 10f, "Speed at which the camera lerps to and from the scoped FOV.");
            ShotDuration = cfg.Bind(Section, "ShotDuration", 0.6f, "Seconds the shot animation plays before the item use state resets.");
            MinZoomFov = cfg.Bind(Section, "MinZoomFov", 5f, "Minimum FOV reachable by scrolling in (maximum zoom). Lower = more zoomed.");
            MaxZoomFov = cfg.Bind(Section, "MaxZoomFov", 40f, "Maximum FOV reachable by scrolling out (minimum zoom). Must be above MinZoomFov.");
            ScrollSensitivity = cfg.Bind(Section, "ScrollSensitivity", 5f, "FOV units changed per scroll notch. Higher values zoom faster.");
            ScreenShakeIntensity = cfg.Bind(Section, "ScreenShakeIntensity", 0.65f, "Intensity of the screen shake when firing the Sniper Rifle. 0 disables it. Higher values shake more.");

            SpawnWeight = global.BindSpawnWeight(cfg, 106, "SniperRifleWeight", 10f, "Override spawn weight for the Sniper Rifle.");
        }
    }
}
