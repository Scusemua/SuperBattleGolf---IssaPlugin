using BepInEx.Configuration;
using UnityEngine.InputSystem;

namespace IssaPlugin
{
    public class Remington870Config
    {
        private const string Section = "Remington870";

        public ConfigEntry<Key> GiveKey { get; private set; }
        public ConfigEntry<float> Uses { get; private set; }
        public ConfigEntry<float> PumpDuration { get; private set; }
        public ConfigEntry<float> PelletCount { get; private set; }
        public ConfigEntry<float> Inaccuracy { get; private set; }
        public ConfigEntry<float> MaxAimingDistance { get; private set; }
        public ConfigEntry<float> MaxShotDistance { get; private set; }
        public ConfigEntry<float> ScreenShakeIntensity { get; private set; }
        public ConfigEntry<float> BonusKnockback { get; private set; }

        public Remington870Config(ConfigFile cfg, GlobalConfig global)
        {
            GiveKey = cfg.Bind(
                Section,
                "GiveKey",
                Key.None,
                "Debug key to add the Remington 870 to your inventory."
            );
            Uses = cfg.Bind(
                Section,
                "Uses",
                6f,
                "Shells per Remington 870 pickup. Each pump consumes one shell."
            );
            PumpDuration = cfg.Bind(
                Section,
                "PumpDuration",
                0.75f,
                "Seconds after a shot during which further clicks are ignored."
            );
            PelletCount = cfg.Bind(
                Section,
                "PelletCount",
                8f,
                "Pellets fired in a cone per shell. Each pellet can hit a different target. One target is hit at most once per shell."
            );
            Inaccuracy = cfg.Bind(
                Section,
                "Inaccuracy",
                6f,
                "Random spread angle (degrees) applied independently to each pellet. Higher = wider cone."
            );
            MaxAimingDistance = cfg.Bind(
                Section,
                "MaxAimingDistance",
                800f,
                "Max distance used when computing the aim point. Pellets still stop at MaxShotDistance."
            );
            MaxShotDistance = cfg.Bind(
                Section,
                "MaxShotDistance",
                36f,
                "Max distance a pellet travels. Past this, the pellet does not hit."
            );
            ScreenShakeIntensity = cfg.Bind(
                Section,
                "ScreenShakeIntensity",
                1.5f,
                "Intensity of the screen shake when firing the Remington 870. 0 disables it."
            );
            BonusKnockback = cfg.Bind(
                Section,
                "BonusKnockback",
                10f,
                "Extra velocity change applied to a player hit by a shell, on top of the elephant-gun knockback. 0 disables the bonus. Applies on the machine that simulates that player."
            );
        }
    }
}
