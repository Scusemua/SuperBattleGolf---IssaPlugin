using System.Globalization;
using BepInEx.Configuration;
using UnityEngine;
using UnityEngine.InputSystem;

namespace IssaPlugin
{
    /// Night Time lighting. Two separate systems are configured here:
    /// the skybox shader (what the sky looks like) and Unity scene lighting
    /// (what illuminates the course). Color entries are "r, g, b" in the 0-1 range.
    public class NightConfig
    {
        private const string Section = "NightTime";

        public static readonly Color DefaultSkyColor = new Color(0f, 0f, 0.02f);
        public static readonly Color DefaultHorizonColor = new Color(0f, 0.01f, 0.04f);
        public static readonly Vector3 DefaultSunDirection = new Vector3(0.3f, -0.9f, 0.3f);
        public const float DefaultMoonCycle = 0.5f;
        public const float DefaultStarsExposure = 50f;
        public const float DefaultSkyAmbient = 0f;
        public static readonly Color DefaultAmbientLight = new Color(0.05f, 0.06f, 0.12f);
        public const float DefaultAmbientIntensity = 0.25f;
        public static readonly Color DefaultSunColor = new Color(0.65f, 0.75f, 1f);
        public const float DefaultSunIntensity = 0.15f;
        public const float DefaultReflectionIntensity = 0.075f;

        public ConfigEntry<Key> GiveKey { get; private set; }
        public ConfigEntry<float> Uses { get; private set; }
        public ConfigEntry<float> Duration { get; private set; }
        public ConfigEntry<bool> ExcludeActivator { get; private set; }
        public ConfigEntry<string> SkyColor { get; private set; }
        public ConfigEntry<string> HorizonColor { get; private set; }
        public ConfigEntry<string> SunDirection { get; private set; }
        public ConfigEntry<float> MoonCycle { get; private set; }
        public ConfigEntry<float> StarsExposure { get; private set; }
        public ConfigEntry<float> SkyAmbient { get; private set; }
        public ConfigEntry<string> AmbientLight { get; private set; }
        public ConfigEntry<float> AmbientIntensity { get; private set; }
        public ConfigEntry<string> SunColor { get; private set; }
        public ConfigEntry<float> SunIntensity { get; private set; }
        public ConfigEntry<float> ReflectionIntensity { get; private set; }

        public Color SkyColorValue => ParseColor(SkyColor.Value, DefaultSkyColor);
        public Color HorizonColorValue => ParseColor(HorizonColor.Value, DefaultHorizonColor);
        public Vector3 SunDirectionValue => ParseVector3(SunDirection.Value, DefaultSunDirection);
        public Color AmbientLightValue => ParseColor(AmbientLight.Value, DefaultAmbientLight);
        public Color SunColorValue => ParseColor(SunColor.Value, DefaultSunColor);

        public NightConfig(ConfigFile cfg, GlobalConfig global)
        {
            GiveKey = cfg.Bind(
                Section,
                "GiveKey",
                Key.None,
                "Debug key to add the Night Time item to your inventory."
            );
            Uses = cfg.Bind(Section, "Uses", 1f, "Number of uses per Night Time pickup.");
            Duration = cfg.Bind(
                Section,
                "Duration",
                20f,
                "Seconds the course stays in night lighting."
            );
            ExcludeActivator = cfg.Bind(
                Section,
                "ExcludeActivator",
                false,
                "When true, the player who uses Night Time keeps normal lighting. "
                    + "Everyone else still sees night. When false, the user sees night too."
            );
            // Skybox shader. These change the sky dome, not the light on the ground.
            SkyColor = cfg.Bind(
                Section,
                "SkyColor",
                FormatColor(DefaultSkyColor),
                "Upper sky color, r, g, b in 0-1. This is the dome above the horizon, not the light on the course."
            );
            HorizonColor = cfg.Bind(
                Section,
                "HorizonColor",
                FormatColor(DefaultHorizonColor),
                "Color of the sky band at the horizon, r, g, b in 0-1."
            );
            SunDirection = cfg.Bind(
                Section,
                "SunDirection",
                FormatVector3(DefaultSunDirection),
                "Direction of the sun disc drawn on the skybox, x, y, z. "
                    + "This does not move the scene light. A negative y puts the disc below the horizon."
            );
            MoonCycle = cfg.Bind(
                Section,
                "MoonCycle",
                DefaultMoonCycle,
                new ConfigDescription(
                    "Moon phase drawn on the skybox. 0 is a new moon, 1 is full.",
                    new AcceptableValueRange<float>(0f, 1f)
                )
            );
            StarsExposure = cfg.Bind(
                Section,
                "StarsExposure",
                DefaultStarsExposure,
                "Brightness of the stars on the skybox. Higher values make the stars easier to see."
            );
            SkyAmbient = cfg.Bind(
                Section,
                "SkyAmbient",
                DefaultSkyAmbient,
                "Ambient term stored on the skybox shader. This tints the sky material. "
                    + "It is separate from AmbientLight, which lights the course."
            );
            // Scene lighting. These change how bright the ground, players, and props are.
            AmbientLight = cfg.Bind(
                Section,
                "AmbientLight",
                FormatColor(DefaultAmbientLight),
                "Flat ambient color filling shadows on the course, r, g, b in 0-1. "
                    + "This is the moonlight on the ground, not the sky color."
            );
            AmbientIntensity = cfg.Bind(
                Section,
                "AmbientIntensity",
                DefaultAmbientIntensity,
                "Strength of AmbientLight. 0 leaves shadows black. Higher values lift the whole course."
            );
            SunColor = cfg.Bind(
                Section,
                "SunColor",
                FormatColor(DefaultSunColor),
                "Color of the directional scene light, r, g, b in 0-1. "
                    + "This is the light casting on the course. It is separate from SunDirection."
            );
            SunIntensity = cfg.Bind(
                Section,
                "SunIntensity",
                DefaultSunIntensity,
                "Brightness of that directional light. Lower values leave the course lit mostly by AmbientLight."
            );
            ReflectionIntensity = cfg.Bind(
                Section,
                "ReflectionIntensity",
                DefaultReflectionIntensity,
                "How bright reflections are on shiny surfaces while night is active. 0 removes them."
            );
        }

        private static string FormatColor(Color c) =>
            string.Format(
                CultureInfo.InvariantCulture,
                "{0}, {1}, {2}",
                c.r,
                c.g,
                c.b
            );

        private static string FormatVector3(Vector3 v) =>
            string.Format(CultureInfo.InvariantCulture, "{0}, {1}, {2}", v.x, v.y, v.z);

        private static Color ParseColor(string raw, Color fallback)
        {
            if (!TrySplit3(raw, out float x, out float y, out float z))
                return fallback;
            return new Color(x, y, z);
        }

        private static Vector3 ParseVector3(string raw, Vector3 fallback)
        {
            if (!TrySplit3(raw, out float x, out float y, out float z))
                return fallback;
            return new Vector3(x, y, z);
        }

        private static bool TrySplit3(string raw, out float x, out float y, out float z)
        {
            x = y = z = 0f;
            if (string.IsNullOrWhiteSpace(raw))
                return false;
            var parts = raw.Split(',');
            if (parts.Length < 3)
                return false;
            return float.TryParse(
                    parts[0].Trim(),
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out x
                )
                && float.TryParse(
                    parts[1].Trim(),
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out y
                )
                && float.TryParse(
                    parts[2].Trim(),
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out z
                );
        }
    }
}
