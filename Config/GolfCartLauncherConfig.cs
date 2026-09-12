using BepInEx.Configuration;
using UnityEngine.InputSystem;

namespace IssaPlugin
{
    public class GolfCartLauncherConfig
    {
        private const string Section = "GolfCartLauncher";

        public ConfigEntry<Key> GiveKey { get; private set; }

        /// <summary>Number of carts per Golf Cart Launcher pickup. Each shot consumes one use.</summary>
        public ConfigEntry<float> Uses { get; private set; }

        /// <summary>Seconds between successive shots while the fire button is held.</summary>
        public ConfigEntry<float> FireRate { get; private set; }

        /// <summary>Speed (m/s) the launched cart is given along the aim direction.</summary>
        public ConfigEntry<float> LaunchSpeed { get; private set; }

        /// <summary>Random spread cone (degrees) applied to each launch. 0 is perfectly accurate.</summary>
        public ConfigEntry<float> Inaccuracy { get; private set; }

        /// <summary>Max distance used when computing the aim point for each shot.</summary>
        public ConfigEntry<float> MaxAimingDistance { get; private set; }

        /// <summary>Distance in front of the player the cart is spawned at.</summary>
        public ConfigEntry<float> SpawnForwardOffset { get; private set; }

        /// <summary>Vertical offset applied to the cart spawn position.</summary>
        public ConfigEntry<float> SpawnVerticalOffset { get; private set; }

        /// <summary>Angular velocity (degrees/sec) applied to the cart about its local right axis (tumble/pitch).</summary>
        public ConfigEntry<float> SpinPitch { get; private set; }

        /// <summary>Angular velocity (degrees/sec) applied to the cart about its local up axis (yaw).</summary>
        public ConfigEntry<float> SpinYaw { get; private set; }

        /// <summary>Angular velocity (degrees/sec) applied to the cart about its local forward axis (barrel roll).</summary>
        public ConfigEntry<float> SpinRoll { get; private set; }

        /// <summary>
        /// Seconds a launched cart survives before the server despawns it.
        /// Prevents launched carts from piling up over a hole. 0 disables the timeout.
        /// </summary>
        public ConfigEntry<float> Lifetime { get; private set; }

        /// <summary>Intensity of the screen shake applied when firing. 0 disables it.</summary>
        public ConfigEntry<float> ScreenShakeIntensity { get; private set; }

        /// <summary>
        /// Key that toggles Joyride mode (ride the launched cart). Only has an effect
        /// while the equipped launcher is at full uses.
        /// </summary>
        public ConfigEntry<Key> JoyrideToggleKey { get; private set; }

        /// <summary>Whether the Joyride HUD indicator is drawn while the launcher is equipped.</summary>
        public ConfigEntry<bool> ShowJoyrideHud { get; private set; }

        /// <summary>Speed (m/s) of a cart launched in Joyride mode, with the player aboard.</summary>
        public ConfigEntry<float> JoyrideLaunchSpeed { get; private set; }

        public GolfCartLauncherConfig(ConfigFile cfg, GlobalConfig global)
        {
            GiveKey = cfg.Bind(
                Section,
                "GiveKey",
                Key.None,
                "Hotkey to give yourself a Golf Cart Launcher (debug/testing)."
            );

            Uses = cfg.Bind(
                Section,
                "Uses",
                5f,
                "Number of carts per Golf Cart Launcher pickup. Each shot consumes one use."
            );

            FireRate = cfg.Bind(
                Section,
                "FireRate",
                0.75f,
                "Seconds between successive shots while the fire button is held."
            );

            LaunchSpeed = cfg.Bind(
                Section,
                "LaunchSpeed",
                55f,
                "Speed (metres/second) the launched golf cart is given along the aim direction."
            );

            Inaccuracy = cfg.Bind(
                Section,
                "Inaccuracy",
                3f,
                "Random spread angle (degrees) applied to each launch. Higher = less accurate. 0 is perfectly accurate."
            );

            MaxAimingDistance = cfg.Bind(
                Section,
                "MaxAimingDistance",
                500f,
                "Max distance used when computing the aim point for each shot."
            );

            SpawnForwardOffset = cfg.Bind(
                Section,
                "SpawnForwardOffset",
                4f,
                "Distance in front of the player the cart is spawned at. Must clear the player's own collider."
            );

            SpawnVerticalOffset = cfg.Bind(
                Section,
                "SpawnVerticalOffset",
                1.5f,
                "Vertical offset applied to the cart spawn position, relative to the player's feet."
            );

            SpinPitch = cfg.Bind(
                Section,
                "SpinPitch",
                360f,
                "Rotation speed (degrees/second) of the launched cart about its local right axis (forward tumble). Negative flips the direction."
            );

            SpinYaw = cfg.Bind(
                Section,
                "SpinYaw",
                0f,
                "Rotation speed (degrees/second) of the launched cart about its local up axis (flat spin)."
            );

            SpinRoll = cfg.Bind(
                Section,
                "SpinRoll",
                0f,
                "Rotation speed (degrees/second) of the launched cart about its local forward axis (barrel roll)."
            );

            Lifetime = cfg.Bind(
                Section,
                "Lifetime",
                300f,
                "Seconds a launched cart survives before the server despawns it. 0 disables the timeout (carts persist until the hole ends)."
            );

            ScreenShakeIntensity = cfg.Bind(
                Section,
                "ScreenShakeIntensity",
                0.425f,
                "Intensity of the screen shake when firing the Golf Cart Launcher. 0 disables it."
            );

            JoyrideToggleKey = cfg.Bind(
                Section,
                "JoyrideToggleKey",
                Key.V,
                "Key that toggles Joyride mode, where firing seats you in the launched cart and consumes the whole item. Only selectable while the launcher is at full uses."
            );

            ShowJoyrideHud = cfg.Bind(
                Section,
                "ShowJoyrideHud",
                true,
                "Show the firing-mode indicator on the HUD while the Golf Cart Launcher is equipped."
            );

            JoyrideLaunchSpeed = cfg.Bind(
                Section,
                "JoyrideLaunchSpeed",
                35f,
                "Speed (metres/second) of a cart launched in Joyride mode. Lower than LaunchSpeed by default, since you are riding it."
            );
        }
    }
}