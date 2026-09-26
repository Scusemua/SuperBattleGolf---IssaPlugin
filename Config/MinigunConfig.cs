using BepInEx.Configuration;
using UnityEngine.InputSystem;

namespace IssaPlugin
{
    public class MinigunConfig
    {
        private const string Section = "Minigun";

        public ConfigEntry<Key> GiveKey { get; private set; }
        public ConfigEntry<float> Uses { get; private set; }
        public ConfigEntry<float> FireRate { get; private set; }
        public ConfigEntry<float> SpinUp { get; private set; }
        public ConfigEntry<float> Inaccuracy { get; private set; }
        public ConfigEntry<float> MaxAimingDistance { get; private set; }
        public ConfigEntry<float> MaxShotDistance { get; private set; }
        public ConfigEntry<float> ScreenShakeIntensity { get; private set; }

        public MinigunConfig(ConfigFile cfg, GlobalConfig global)
        {
            GiveKey = cfg.Bind(
                Section,
                "GiveKey",
                Key.None,
                "Debug key to add the Minigun to your inventory."
            );
            Uses = cfg.Bind(
                Section,
                "Uses",
                500f,
                "Bullets per Minigun pickup. Each bullet consumes one use. The spin-up spends none."
            );
            FireRate = cfg.Bind(
                Section,
                "FireRate",
                0.02f,
                "Seconds between bullets once the barrels are spun up."
            );
            SpinUp = cfg.Bind(
                Section,
                "SpinUp",
                0.4f,
                "Seconds the barrels spin before the first bullet. Releasing the left mouse button during this wind-up spends no ammo. 0 fires immediately."
            );
            Inaccuracy = cfg.Bind(
                Section,
                "Inaccuracy",
                8f,
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
                0.75f,
                "Intensity of the screen shake per bullet. 0 disables it. This gun fires quickly, so keep it low."
            );
        }
    }
}
