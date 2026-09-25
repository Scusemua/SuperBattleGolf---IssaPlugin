using System.Globalization;
using BepInEx.Configuration;
using UnityEngine;
using UnityEngine.InputSystem;

namespace IssaPlugin
{
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
        public const float DefaultReflectionIntensity = 0.05f;

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
            SkyColor = cfg.Bind(
                Section,
                "SkyColor",
                FormatColor(DefaultSkyColor),
                "Sky color as r, g, b in the 0-1 range."
            );
            HorizonColor = cfg.Bind(
                Section,
                "HorizonColor",
                FormatColor(DefaultHorizonColor),
                "Horizon color as r, g, b in the 0-1 range."
            );
            SunDirection = cfg.Bind(
                Section,
                "SunDirection",
                FormatVector3(DefaultSunDirection),
                "Skybox sun direction as x, y, z. A negative y puts the sun below the horizon."
            );
            MoonCycle = cfg.Bind(
                Section,
                "MoonCycle",
                DefaultMoonCycle,
                new ConfigDescription(
                    "Moon phase on the skybox. 0 is new, 1 is full.",
                    new AcceptableValueRange<float>(0f, 1f)
                )
            );
            StarsExposure = cfg.Bind(
                Section,
                "StarsExposure",
                DefaultStarsExposure,
                "How bright the skybox stars are."
            );
            SkyAmbient = cfg.Bind(
                Section,
                "SkyAmbient",
                DefaultSkyAmbient,
                "Ambient contribution stored on the skybox shader."
            );
            AmbientLight = cfg.Bind(
                Section,
                "AmbientLight",
                FormatColor(DefaultAmbientLight),
                "Scene ambient light as r, g, b in the 0-1 range."
            );
            AmbientIntensity = cfg.Bind(
                Section,
                "AmbientIntensity",
                DefaultAmbientIntensity,
                "Scene ambient intensity while night is active."
            );
            SunColor = cfg.Bind(
                Section,
                "SunColor",
                FormatColor(DefaultSunColor),
                "Directional light color as r, g, b in the 0-1 range."
            );
            SunIntensity = cfg.Bind(
                Section,
                "SunIntensity",
                DefaultSunIntensity,
                "Directional light intensity while night is active."
            );
            ReflectionIntensity = cfg.Bind(
                Section,
                "ReflectionIntensity",
                DefaultReflectionIntensity,
                "Reflection intensity while night is active."
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
