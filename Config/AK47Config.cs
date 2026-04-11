using BepInEx.Configuration;
using UnityEngine.InputSystem;

namespace IssaPlugin
{
    public class AK47Config
    {
        private const string Section = "AK47";

        public ConfigEntry<Key> GiveKey { get; private set; }
        public ConfigEntry<float> Uses { get; private set; }
        public ConfigEntry<float> FireRate { get; private set; }
        public ConfigEntry<float> Inaccuracy { get; private set; }
        public ConfigEntry<float> MaxAimingDistance { get; private set; }
        public ConfigEntry<float> MaxShotDistance { get; private set; }
        public ConfigEntry<float> ScreenShakeIntensity { get; private set; }

        public AK47Config(ConfigFile cfg, GlobalConfig global)
        {
            GiveKey = cfg.Bind(
                Section,
                "GiveKey",
                Key.NumpadPlus,
                "Debug key to add the Sub-Machine Gun to your inventory."
            );
            Uses = cfg.Bind(
                Section,
                "Uses",
                10f,
                "Total bullets per Sub-Machine Gun pickup. Each shot consumes one use."
            );
            FireRate = cfg.Bind(
                Section,
                "FireRate",
                0.04f,
                "Seconds between each bullet in the burst."
            );
            Inaccuracy = cfg.Bind(
                Section,
                "Inaccuracy",
                5f,
                "Random spread angle (degrees) applied to each bullet. Higher = less accurate."
            );
            MaxAimingDistance = cfg.Bind(
                Section,
                "MaxAimingDistance",
                500f,
                "Max distance used when computing the aim point for each bullet."
            );
            MaxShotDistance = cfg.Bind(
                Section,
                "MaxShotDistance",
                500f,
                "Max raycast distance for each bullet."
            );
            ScreenShakeIntensity = cfg.Bind(
                Section,
                "ScreenShakeIntensity",
                0.25f,
                "Intensity of the screen shake when firing the AK47. 0 disables it. Higher values shake more."
            );
        }
    }
}
