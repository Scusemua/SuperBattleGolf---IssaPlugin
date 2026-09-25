using BepInEx.Configuration;
using UnityEngine.InputSystem;

namespace IssaPlugin
{
    public class EvilGloveConfig
    {
        private const string Section = "EvilGlove";

        public ConfigEntry<Key> GiveKey { get; private set; }
        public ConfigEntry<float> Uses { get; private set; }
        public ConfigEntry<float> HoldDuration { get; private set; }
        public ConfigEntry<float> ChargeDuration { get; private set; }
        public ConfigEntry<float> MinimumThrowSpeed { get; private set; }
        public ConfigEntry<float> MaximumThrowSpeed { get; private set; }
        public ConfigEntry<float> ThrowUpwardBias { get; private set; }
        public ConfigEntry<float> KnockoutEjectSpeed { get; private set; }
        public ConfigEntry<float> MaxAimAngle { get; private set; }
        public ConfigEntry<float> MaxTargetDistance { get; private set; }

        public EvilGloveConfig(ConfigFile cfg, GlobalConfig global)
        {
            GiveKey = cfg.Bind(
                Section,
                "GiveKey",
                Key.None,
                "Debug key to add the Evil Glove to your inventory."
            );
            Uses = cfg.Bind(
                Section,
                "Uses",
                1f,
                "Number of Evil Glove uses. One use is consumed when a hold ends — not on pickup."
            );
            HoldDuration = cfg.Bind(
                Section,
                "HoldDuration",
                8f,
                "Seconds the ball can be held before it is automatically dropped at the player's feet."
            );
            ChargeDuration = cfg.Bind(
                Section,
                "ChargeDuration",
                1.25f,
                "Seconds to fill the throw charge meter from empty to full."
            );
            MinimumThrowSpeed = cfg.Bind(
                Section,
                "MinimumThrowSpeed",
                8f,
                "Throw speed at zero charge."
            );
            MaximumThrowSpeed = cfg.Bind(
                Section,
                "MaximumThrowSpeed",
                28f,
                "Throw speed at full charge. Also used as the server clamp ceiling."
            );
            ThrowUpwardBias = cfg.Bind(
                Section,
                "ThrowUpwardBias",
                0.35f,
                "Upward component mixed into the aim direction for a lob arc. 0 = flat, 1 ≈ 45°."
            );
            KnockoutEjectSpeed = cfg.Bind(
                Section,
                "KnockoutEjectSpeed",
                14f,
                "Speed applied to the ball when the holder is knocked out while carrying it."
            );
            MaxAimAngle = cfg.Bind(
                Section,
                "MaxAimAngle",
                18f,
                "Maximum angle (degrees) from the aim ray to a golf ball for lock-on. "
                    + "Smaller = stricter aim required."
            );
            MaxTargetDistance = cfg.Bind(
                Section,
                "MaxTargetDistance",
                40f,
                "Maximum distance from the camera to a golf ball for lock-on."
            );
        }
    }
}
