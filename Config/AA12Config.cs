using BepInEx.Configuration;
using UnityEngine.InputSystem;

namespace IssaPlugin
{
    public class AA12Config
    {
        private const string Section = "AA12";

        public ConfigEntry<Key> GiveKey { get; private set; }
        public ConfigEntry<float> Uses { get; private set; }
        public ConfigEntry<float> FireRate { get; private set; }
        public ConfigEntry<float> PelletCount { get; private set; }
        public ConfigEntry<float> Inaccuracy { get; private set; }
        public ConfigEntry<float> MaxAimingDistance { get; private set; }
        public ConfigEntry<float> MaxShotDistance { get; private set; }
        public ConfigEntry<float> ScreenShakeIntensity { get; private set; }
        public ConfigEntry<float> BonusKnockback { get; private set; }
        public ConfigEntry<float> BulletPrefab { get; private set; }

        public AA12Config(ConfigFile cfg, GlobalConfig global)
        {
            GiveKey = cfg.Bind(
                Section,
                "GiveKey",
                Key.None,
                "Debug key to add the AA-12 to your inventory."
            );
            Uses = cfg.Bind(
                Section,
                "Uses",
                8f,
                "Shells per AA-12 pickup. Each trigger tick consumes one shell."
            );
            FireRate = cfg.Bind(
                Section,
                "FireRate",
                0.18f,
                "Seconds between shells while the fire button is held."
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
                8f,
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
                26f,
                "Max distance a pellet travels. Past this, the pellet does not hit."
            );
            ScreenShakeIntensity = cfg.Bind(
                Section,
                "ScreenShakeIntensity",
                1.0f,
                "Intensity of the screen shake when firing the AA-12. 0 disables it."
            );
            BonusKnockback = cfg.Bind(
                Section,
                "BonusKnockback",
                8f,
                "Extra velocity change applied to a player hit by a shell, on top of the elephant-gun knockback. 0 disables the bonus. Applies on the machine that simulates that player."
            );
            BulletPrefab = cfg.Bind(
                Section,
                "BulletPrefab",
                1.0f,
                new ConfigDescription(
                    "Bullet prefab 1 or 2.",
                    new AcceptableValueRange<float>(1f, 2f)
                )
            );
        }
    }
}
