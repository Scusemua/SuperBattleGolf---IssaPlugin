using BepInEx.Configuration;
using UnityEngine.InputSystem;

namespace IssaPlugin
{
    public class SuperJumboBurgerConfig
    {
        private const string Section = "SuperJumboBurger";

        public ConfigEntry<Key> GiveKey { get; private set; }
        public ConfigEntry<float> Uses { get; private set; }
        public ConfigEntry<float> Scale { get; private set; }
        public ConfigEntry<float> Duration { get; private set; }
        public ConfigEntry<float> GrowDuration { get; private set; }
        public ConfigEntry<float> CameraDistancePerScale { get; private set; }
        public ConfigEntry<float> CameraHeightPerScale { get; private set; }
        public ConfigEntry<float> SpeedScaling { get; private set; }

        public SuperJumboBurgerConfig(ConfigFile cfg, GlobalConfig global)
        {
            Scale = cfg.Bind(
                Section,
                "Scale",
                8f,
                new ConfigDescription(
                    "How large the player becomes. The base game's Jumbo Burger is 3. "
                        + "Values far above ~8 make the player wider than many fairways.",
                    new AcceptableValueRange<float>(1.1f, 12f)
                )
            );
            Duration = cfg.Bind(
                Section,
                "Duration",
                20f,
                new ConfigDescription(
                    "Seconds spent giant before shrinking back.",
                    new AcceptableValueRange<float>(1f, 300f)
                )
            );
            GrowDuration = cfg.Bind(
                Section,
                "GrowDuration",
                1f,
                new ConfigDescription(
                    "Seconds the grow/shrink animation takes.",
                    new AcceptableValueRange<float>(0.05f, 5f)
                )
            );
            CameraDistancePerScale = cfg.Bind(
                Section,
                "CameraDistancePerScale",
                3.5f,
                new ConfigDescription(
                    "Extra camera distance per unit of scale above the base game's giant "
                        + "scale. The base game's own pull-back is sized for its 3x form, so "
                        + "without this the camera looks straight at a larger player's back. "
                        + "Raise it if the player still fills the screen.",
                    new AcceptableValueRange<float>(0f, 20f)
                )
            );
            CameraHeightPerScale = cfg.Bind(
                Section,
                "CameraHeightPerScale",
                1.2f,
                new ConfigDescription(
                    "Extra camera height per unit of scale above the base game's giant "
                        + "scale. Raises the point the camera looks at, which lifts the "
                        + "camera and angles it down so you can see the course ahead "
                        + "instead of the back of your own head. 0 keeps it level.",
                    new AcceptableValueRange<float>(0f, 20f)
                )
            );
            SpeedScaling = cfg.Bind(
                Section,
                "SpeedScaling",
                0.30f,
                new ConfigDescription(
                    "How much the giant's movement speed grows with size. 0 = no bonus "
                        + "beyond the base game's giant-form boost (a 10x giant feels "
                        + "sluggish). 1 = speed scales fully in proportion to scale, so a "
                        + "10x giant covers ground 10x as fast. 0.30 keeps big feeling "
                        + "heavy without feeling slow.",
                    new AcceptableValueRange<float>(0f, 1f)
                )
            );
            Uses = cfg.Bind(Section, "Uses", 1f, "Number of uses");
            GiveKey = cfg.Bind(
                Section,
                "GiveKey",
                Key.None,
                "Key to press to get the super jumbo burger item (None = no hotkey)."
            );
        }
    }
}
