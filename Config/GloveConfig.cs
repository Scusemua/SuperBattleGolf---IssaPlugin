using BepInEx.Configuration;
using UnityEngine.InputSystem;

namespace IssaPlugin
{
    public class GloveConfig
    {
        private const string Section = "Glove";

        public ConfigEntry<Key> GiveKey { get; private set; }
        public ConfigEntry<float> Uses { get; private set; }
        public ConfigEntry<float> PickupRadius { get; private set; }
        public ConfigEntry<float> HoldDuration { get; private set; }
        public ConfigEntry<float> ChargeDuration { get; private set; }
        public ConfigEntry<float> MinimumThrowSpeed { get; private set; }
        public ConfigEntry<float> MaximumThrowSpeed { get; private set; }
        public ConfigEntry<float> ThrowUpwardBias { get; private set; }
        public ConfigEntry<float> KnockoutEjectSpeed { get; private set; }
        public ConfigEntry<float> IndicatorHeight { get; private set; }

        public GloveConfig(ConfigFile cfg, GlobalConfig global)
        {
            GiveKey = cfg.Bind(
                Section,
                "GiveKey",
                Key.None,
                "Debug key to add the Glove to your inventory."
            );
            Uses = cfg.Bind(Section, "Uses", 2f, "Number of Glove uses per pickup.");
            PickupRadius = cfg.Bind(
                Section,
                "PickupRadius",
                2.5f,
                "Maximum distance from the player to their own ball to allow pickup."
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
            IndicatorHeight = cfg.Bind(
                Section,
                "IndicatorHeight",
                0.7f,
                "Height in Unity units above the holder's HeadBone for the held-ball indicator. "
                    + "Raise this if the icon sits inside the head mesh."
            );
        }
    }
}
